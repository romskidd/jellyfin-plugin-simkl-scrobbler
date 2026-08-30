using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Real-time playback scrobbler.
    /// </summary>
    /// <remarks>
    /// Implements the Simkl scrobble lifecycle (<c>start</c> / <c>pause</c> / <c>stop</c>).
    /// Events are sent only on real player transitions — playback start, pause,
    /// resume and stop — never on a timer. Simkl extrapolates progress between
    /// events from the item runtime, so polling would only waste quota and trip
    /// the 20-second per-user lock. The watched mark is decided server-side: a
    /// <c>stop</c> with progress &gt;= 80 is recorded as watched, anything lower is
    /// kept as a resumable playback.
    /// </remarks>
    public class PlaybackScrobbler : IHostedService
    {
        // Skip re-sending the same action for the same item within this window
        // to stay clear of the server's 20-second per-user lock.
        private static readonly TimeSpan _minActionInterval = TimeSpan.FromSeconds(20);

        // Plugin id of the successor "Simkl Scrobbler" plugin. When it is installed,
        // this legacy plugin stays dormant so playbacks aren't scrobbled twice.
        private static readonly Guid _successorPluginId = new Guid("03A7C840-6154-471F-8BE3-856CDC26D500");

        private readonly ISessionManager _sessionManager;
        private readonly ILogger<PlaybackScrobbler> _logger;
        private readonly SimklApi _simklApi;
        private readonly ILibraryManager _libraryManager;
        private readonly IPluginManager _pluginManager;
        private readonly ConcurrentDictionary<string, SessionState> _sessions;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackScrobbler"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{PlaybackScrobbler}"/> interface.</param>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        /// <param name="pluginManager">Instance of the <see cref="IPluginManager"/> interface.</param>
        public PlaybackScrobbler(
            ISessionManager sessionManager,
            ILogger<PlaybackScrobbler> logger,
            SimklApi simklApi,
            ILibraryManager libraryManager,
            IPluginManager pluginManager)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _simklApi = simklApi;
            _libraryManager = libraryManager;
            _pluginManager = pluginManager;
            _sessions = new ConcurrentDictionary<string, SessionState>();
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_pluginManager.Plugins.Any(p => p.Id == _successorPluginId))
            {
                _logger.LogWarning(
                    "Simkl Scrobbler (the successor of this plugin) is installed: "
                    + "this legacy Simkl plugin stays dormant and can safely be uninstalled.");
                return Task.CompletedTask;
            }

            _logger.LogWarning(
                "This Simkl plugin has MOVED to 'Simkl Scrobbler' (new plugin id) and will not receive "
                + "updates anymore. Install 'Simkl Scrobbler' from the same plugin repository, then "
                + "uninstall this one. Your Simkl login and settings are kept. "
                + "Scrobbling keeps working in the meantime.");

            _sessionManager.PlaybackStart += OnPlaybackStart;
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _sessionManager.PlaybackStart -= OnPlaybackStart;
            _sessionManager.PlaybackProgress -= OnPlaybackProgress;
            _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            return Task.CompletedTask;
        }

        private static bool TypeEnabled(UserConfig config, BaseItemKind type)
        {
            return type switch
            {
                BaseItemKind.Movie => config.ScrobbleMovies,
                BaseItemKind.Episode => config.ScrobbleShows,
                BaseItemKind.Series => config.ScrobbleShows,
                _ => false
            };
        }

        private static bool LongEnough(UserConfig config, PlaybackProgressEventArgs e)
        {
            var runtime = e.MediaInfo?.RunTimeTicks;

            // No runtime info: don't block, let the server decide.
            if (runtime == null)
            {
                return true;
            }

            // MinLength is in minutes; 1 minute == 60 * 10_000_000 ticks.
            return runtime >= 60L * 10_000_000L * config.MinLength;
        }

        private static double GetProgress(PlaybackProgressEventArgs e)
        {
            var position = e.PlaybackPositionTicks;
            var runtime = e.MediaInfo?.RunTimeTicks;
            if (position == null || runtime == null || runtime <= 0)
            {
                return 0d;
            }

            return position.Value / (double)runtime.Value * 100d;
        }

        private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                await HandleStartOrResume(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on PlaybackStart");
            }
        }

        private async void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
        {
            try
            {
                if (e.Session == null || e.MediaInfo == null)
                {
                    return;
                }

                var sessionId = e.Session.Id;
                var paused = e.IsPaused;

                // First time we see this session (or the item changed): evaluate it once.
                // HandleStartOrResume records the session either way, so we won't land
                // back here every tick.
                if (!_sessions.TryGetValue(sessionId, out var state) || state.ItemId != e.MediaInfo.Id)
                {
                    await HandleStartOrResume(e).ConfigureAwait(false);
                    return;
                }

                // Session already judged not scrobblable: skip silently.
                if (state.Ignore)
                {
                    return;
                }

                // React only to pause/resume transitions — never to plain progress
                // ticks or seeks. Local progress is read fresh from the event each time.
                if (paused && !state.Paused)
                {
                    await SendScrobble(SimklScrobbleAction.Pause, e, GetProgress(e)).ConfigureAwait(false);
                    state.Paused = true;
                }
                else if (!paused && state.Paused)
                {
                    await SendScrobble(SimklScrobbleAction.Start, e, GetProgress(e)).ConfigureAwait(false);
                    state.Paused = false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on PlaybackProgress");
            }
        }

        private async void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
        {
            try
            {
                if (e.Session == null || e.MediaInfo == null)
                {
                    return;
                }

                // If the session was judged not scrobblable at start, don't try to
                // close it out (that would log again for a not-logged-in user).
                if (_sessions.TryGetValue(e.Session.Id, out var existing) && existing.Ignore)
                {
                    return;
                }

                // A natural end-of-playback reports 100%; otherwise use the last position.
                var progress = e.PlayedToCompletion ? 100d : GetProgress(e);

                // Always attempt a stop, even if filtered for start, so a paused
                // session is correctly closed out.
                await SendScrobble(SimklScrobbleAction.Stop, e, progress, force: true).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception on PlaybackStopped");
            }
            finally
            {
                if (e.Session != null)
                {
                    _sessions.TryRemove(e.Session.Id, out _);
                }
            }
        }

        private async Task HandleStartOrResume(PlaybackProgressEventArgs e)
        {
            if (e.Session == null || e.MediaInfo == null)
            {
                return;
            }

            var session = e.Session;

            // Decide eligibility once per session+item. These conditions don't change
            // mid-playback, so caching the decision stops the per-second re-evaluation
            // (and the per-second "not logged in" log spam) that happened when an
            // ineligible session was never recorded.
            var userConfig = SimklPlugin.Instance?.Configuration.GetByGuid(session.UserId);
            string? skipReason = null;
            if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
            {
                skipReason = "user " + session.UserName + " not logged in to Simkl";
            }
            else if (!userConfig.EnablePlaybackScrobbling)
            {
                skipReason = "real-time scrobbling disabled for " + session.UserName;
            }
            else if (!TypeEnabled(userConfig, e.MediaInfo.Type) || !LongEnough(userConfig, e))
            {
                skipReason = "item filtered by type/length";
            }

            // Record the session up front, eligible or not, so progress ticks recognise
            // it and stop reprocessing.
            var state = new SessionState
            {
                ItemId = e.MediaInfo.Id,
                Paused = e.IsPaused,
                Ignore = skipReason != null
            };
            _sessions[session.Id] = state;

            if (skipReason != null)
            {
                // Logged exactly once per session+item, at debug level so it never
                // floods the main log.
                _logger.LogDebug("Not scrobbling: {Reason}", skipReason);
                return;
            }

            if (!e.IsPaused
                && await SendScrobble(SimklScrobbleAction.Start, e, GetProgress(e)).ConfigureAwait(false))
            {
                state.LastAction = SimklScrobbleAction.Start;
                state.LastSent = DateTime.UtcNow;
            }
        }

        private async Task<bool> SendScrobble(SimklScrobbleAction action, PlaybackProgressEventArgs e, double progress, bool force = false)
        {
            var session = e.Session;
            var mediaInfo = e.MediaInfo;
            if (session == null || mediaInfo == null)
            {
                return false;
            }

            var userId = session.UserId;
            var userConfig = SimklPlugin.Instance?.Configuration.GetByGuid(userId);
            if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
            {
                _logger.LogDebug("Can't scrobble: user {UserName} not logged in", session.UserName);
                return false;
            }

            if (!userConfig.EnablePlaybackScrobbling)
            {
                return false;
            }

            if (!TypeEnabled(userConfig, mediaInfo.Type) || !LongEnough(userConfig, e))
            {
                return false;
            }

            // Debounce duplicate calls to dodge the 20s lock. Stop is never debounced.
            if (!force && action != SimklScrobbleAction.Stop
                && _sessions.TryGetValue(session.Id, out var state)
                && state.LastAction == action
                && state.ItemId == mediaInfo.Id
                && DateTime.UtcNow - state.LastSent < _minActionInterval)
            {
                return false;
            }

            try
            {
                _logger.LogInformation(
                    "Scrobble {Action} {Name} ({Progress:0.##}%) for {UserName}",
                    action,
                    mediaInfo.Name,
                    progress,
                    session.UserName);

                var success = await _simklApi.ScrobbleAsync(
                        action, mediaInfo, progress, userConfig.UserToken, ResolveSeriesProviderIds(mediaInfo))
                    .ConfigureAwait(false);

                if (success && _sessions.TryGetValue(session.Id, out var s))
                {
                    s.LastAction = action;
                    s.LastSent = DateTime.UtcNow;
                }

                return success;
            }
            catch (InvalidTokenException)
            {
                _logger.LogDebug("Deleted invalid user token");
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Couldn't scrobble {Name}", mediaInfo.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Caught unknown exception while trying to scrobble");
            }

            return false;
        }

        /// <summary>
        /// Resolves the parent series' provider ids for an episode item.
        /// </summary>
        /// <remarks>
        /// Episodes carry their own IMDB/TVDB/TMDB ids, which are different from
        /// the series-level ids that Simkl needs to identify the show. This method
        /// looks up the parent series via the library and returns its provider ids.
        /// Returns null for non-episode items (movies use their own ids directly).
        /// </remarks>
        private Dictionary<string, string>? ResolveSeriesProviderIds(MediaBrowser.Model.Dto.BaseItemDto item)
        {
            if (item.Type != BaseItemKind.Episode || item.SeriesId == null)
            {
                return null;
            }

            try
            {
                var series = _libraryManager.GetItemById(item.SeriesId.Value);
                var providerIds = series?.ProviderIds;
                if (providerIds is { Count: > 0 })
                {
                    _logger.LogDebug(
                        "Resolved series IDs for {Episode}: {Ids}",
                        item.Name,
                        string.Join(", ", providerIds));
                    return new Dictionary<string, string>(providerIds, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve series for episode {Name}", item.Name);
            }

            return null;
        }

        /// <summary>
        /// Per-session scrobble state.
        /// </summary>
        private sealed class SessionState
        {
            /// <summary>
            /// Gets or sets the id of the item currently playing in this session.
            /// </summary>
            public Guid ItemId { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether the session is currently paused.
            /// </summary>
            public bool Paused { get; set; }

            /// <summary>
            /// Gets or sets a value indicating whether this session has been evaluated
            /// and judged not scrobblable (not logged in, scrobbling disabled, or the
            /// item is filtered). When true the session is tracked but skipped silently
            /// so progress ticks don't re-evaluate or re-log it every second.
            /// </summary>
            public bool Ignore { get; set; }

            /// <summary>
            /// Gets or sets the last scrobble action that was successfully sent.
            /// </summary>
            public SimklScrobbleAction LastAction { get; set; }

            /// <summary>
            /// Gets or sets the UTC time of the last successful scrobble.
            /// </summary>
            public DateTime LastSent { get; set; }
        }
    }
}
