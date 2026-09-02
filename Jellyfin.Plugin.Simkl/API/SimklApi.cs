using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Simkl.API.Exceptions;
using Jellyfin.Plugin.Simkl.API.Objects;
using Jellyfin.Plugin.Simkl.API.Objects.Scrobble;
using Jellyfin.Plugin.Simkl.API.Responses;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Simkl Api.
    /// </summary>
    public class SimklApi
    {
        /* INTERFACES */
        private readonly ILogger<SimklApi> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ConcurrentDictionary<string, PostGate> _postGates = new ConcurrentDictionary<string, PostGate>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CachedStats> _statsCache = new ConcurrentDictionary<string, CachedStats>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CachedSettings> _settingsCache = new ConcurrentDictionary<string, CachedSettings>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ActivitySnapshot> _activityCache = new ConcurrentDictionary<string, ActivitySnapshot>(StringComparer.Ordinal);
        private readonly JsonSerializerOptions _jsonSerializerOptions;
        private readonly JsonSerializerOptions _caseInsensitiveJsonSerializerOptions;

        /* BASIC API THINGS */

        /// <summary>
        /// Base url.
        /// </summary>
        public const string Baseurl = @"https://api.simkl.com";

        /// <summary>
        /// Redirect uri.
        /// </summary>
        public const string RedirectUri = @"https://romskidd.github.io/jellyfin-plugin-simkl-scrobbler/connected.html";

        /// <summary>
        /// Api key.
        /// </summary>
        public const string Apikey = @"f07edb607a8d4f3a19ecbffaefa33192f3e753c684c153595ae458bd580ab70c";

        /// <summary>
        /// App identifier sent with every request, as required by the Simkl API
        /// (see api.simkl.org/conventions/headers).
        /// </summary>
        private const string AppName = "Simkl Scrobbler";

        /// <summary>
        /// Plugin version reported to Simkl alongside <see cref="AppName"/>.
        /// </summary>
        private static readonly string _appVersion =
            typeof(SimklApi).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

        /// <summary>
        /// Query string identifying this app, appended to every request URI.
        /// Simkl requires client_id, app-name and app-version as URL parameters.
        /// </summary>
        private static readonly string _identityQuery =
            "client_id=" + Apikey + "&app-name=" + Uri.EscapeDataString(AppName) + "&app-version=" + _appVersion;

        /// <summary>
        /// Simkl allows one authenticated POST per second per user. Two writes
        /// back to back (a lookup then a scrobble, a stop then a rewatch) would
        /// trip a temporary block on the token, so writes are spaced out.
        /// </summary>
        private static readonly TimeSpan _minPostInterval = TimeSpan.FromSeconds(1.1);

        /// <summary>
        /// How long one reading of <c>GET /sync/activities</c> is reused. It
        /// absorbs the bursts of a page load, where settings and statistics are
        /// needed within seconds, so a page load costs one activity call at most.
        /// </summary>
        private static readonly TimeSpan _activityMemo = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklApi"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{SimklApi}"/> interface.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        public SimklApi(ILogger<SimklApi> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _jsonSerializerOptions = JsonDefaults.Options;
            _caseInsensitiveJsonSerializerOptions = new JsonSerializerOptions(_jsonSerializerOptions)
            {
                PropertyNameCaseInsensitive = true
            };
        }

        /// <summary>
        /// Get code.
        /// </summary>
        /// <returns>Code response.</returns>
        public async Task<CodeResponse?> GetCode()
        {
            var uri = "/oauth/pin?redirect=" + Uri.EscapeDataString(RedirectUri);
            return await Get<CodeResponse>(uri);
        }

        /// <summary>
        /// Get code status.
        /// </summary>
        /// <param name="userCode">User code.</param>
        /// <returns>Code status.</returns>
        public async Task<CodeStatusResponse?> GetCodeStatus(string userCode)
        {
            // The code comes from a route parameter: escape it so it can only
            // ever be a path segment, never redirect the request elsewhere.
            var uri = $"/oauth/pin/{Uri.EscapeDataString(userCode)}";
            return await Get<CodeStatusResponse>(uri);
        }

        /// <summary>
        /// Get user settings.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <returns>User settings.</returns>
        public async Task<UserSettings?> GetUserSettings(string userToken)
        {
            // Simkl asks apps to read /users/settings only when the activity
            // endpoint says the settings changed. The snapshot lives in memory
            // and in the user's configuration, so a restart doesn't cost a read.
            _settingsCache.TryGetValue(userToken, out var cached);
            if (cached == null)
            {
                cached = LoadStoredSettings(userToken);
                if (cached != null)
                {
                    _settingsCache[userToken] = cached;
                }
            }

            var activity = await GetActivityAsync(userToken).ConfigureAwait(false);
            if (activity.Unauthorized)
            {
                // A token Simkl no longer accepts (revoked, or issued to another
                // application) is dropped at once, so the pages show "link
                // expired" instead of a connected account that can't scrobble.
                DropToken(userToken);
                return new UserSettings { Error = "user_token_failed" };
            }

            if (cached != null
                && (activity.SettingsStamp == null
                    || string.Equals(activity.SettingsStamp, cached.Stamp, StringComparison.Ordinal)))
            {
                // Unchanged, or the activity call failed transiently: the snapshot stays good.
                return cached.Settings;
            }

            try
            {
                var settings = await Post<UserSettings, object>("/users/settings/", userToken);
                if (string.Equals(settings?.Error, "user_token_failed", StringComparison.Ordinal))
                {
                    DropToken(userToken);
                    return settings;
                }

                if (settings != null && settings.Account?.Id != null)
                {
                    var entry = new CachedSettings(settings, activity.SettingsStamp);
                    _settingsCache[userToken] = entry;
                    StoreSettings(userToken, entry);
                }

                return settings;
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                DropToken(userToken);
                // Wontfix: Custom status codes
                // "You don't get to pick your response code" - Luke (System Architect of Emby)
                // https://emby.media/community/index.php?/topic/61889-wiki-issue-resultfactorythrowerror/
                return new UserSettings { Error = "user_token_failed" };
            }
        }

        /// <summary>
        /// Mark as watched.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>Status.</returns>
        public async Task<(bool Success, BaseItemDto Item)> MarkAsWatched(BaseItemDto item, string userToken)
        {
            var history = CreateHistoryFromItem(item);
            var r = await SyncHistoryAsync(history, userToken);
            _logger.LogDebug("BaseItem: {@Item}", item);
            _logger.LogDebug("History: {@History}", history);
            _logger.LogDebug("Response: {@Response}", r);
            if (r != null && history.Movies.Count == r.Added.Movies
                && history.Shows.Count == r.Added.Shows
                && history.Episodes.Count == r.Added.Episodes)
            {
                return (true, item);
            }

            // If we are here, is because the item has not been found
            // let's try scrobbling from full path
            try
            {
                (history, item) = await GetHistoryFromFileName(item);
            }
            catch (InvalidDataException)
            {
                // Let's try again but this time using only the FILE name
                _logger.LogDebug("Couldn't scrobble using full path, trying using only filename");
                (history, item) = await GetHistoryFromFileName(item, false);
            }

            r = await SyncHistoryAsync(history, userToken);
            return r == null
                ? (false, item)
                : (history.Movies.Count == r.Added.Movies && history.Shows.Count == r.Added.Shows, item);
        }

        /// <summary>
        /// Reports a real-time scrobble event (start / pause / stop) to Simkl.
        /// </summary>
        /// <remarks>
        /// Implements the <c>/scrobble/{action}</c> lifecycle. The body is built from
        /// the item's provider ids first; if Simkl can't resolve them (<c>404</c>) it
        /// falls back to a filename search exactly like <see cref="MarkAsWatched"/>.
        /// </remarks>
        /// <param name="action">The scrobble action.</param>
        /// <param name="item">The item being played.</param>
        /// <param name="progress">Playback progress as a percentage (0 to 100).</param>
        /// <param name="userToken">User token.</param>
        /// <param name="seriesProviderIds">Optional series-level provider ids, resolved
        /// by the caller when the item is an episode. When present these are used for
        /// the <c>show</c> object instead of the episode's own ids.</param>
        /// <returns><c>true</c> if Simkl accepted the event.</returns>
        public async Task<bool> ScrobbleAsync(SimklScrobbleAction action, BaseItemDto item, double progress, string userToken, Dictionary<string, string>? seriesProviderIds = null)
        {
            var body = BuildScrobbleBody(item, progress, seriesProviderIds);
            if (body == null)
            {
                _logger.LogDebug("Nothing to scrobble for {Name} ({Type})", item.Name, item.Type);
                return false;
            }

            var status = await PostScrobble(action, body, userToken).ConfigureAwait(false);
            if (IsScrobbleSuccess(status))
            {
                return true;
            }

            // The 20-second per-user lock or a transient error: don't retry now,
            // the next real player event will cover it.
            if (status != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Scrobble {Action} returned {Status}, will retry on next event", action, status);
                return false;
            }

            // 404 id_err: the ids didn't resolve. Try a filename match (full path, then file name only).
            _logger.LogDebug("Scrobble ids didn't resolve, trying filename match for {Path}", item.Path);
            try
            {
                body = await BuildScrobbleBodyFromFile(item, progress, true).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                body = await BuildScrobbleBodyFromFile(item, progress, false).ConfigureAwait(false);
            }

            if (body == null)
            {
                return false;
            }

            status = await PostScrobble(action, body, userToken).ConfigureAwait(false);
            return IsScrobbleSuccess(status);
        }

        /// <summary>
        /// Adds the given items to the user's Simkl watch history
        /// (<c>POST /sync/history</c>).
        /// </summary>
        /// <param name="history">History object.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>The sync history response.</returns>
        public async Task<SyncHistoryResponse?> AddToHistory(SimklHistory history, string userToken)
        {
            return await SyncHistoryAsync(history, userToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Asks Simkl whether the user has already watched an item
        /// (<c>POST /sync/watched</c>).
        /// </summary>
        /// <remarks>
        /// Simkl is the authority here, not Jellyfin: something watched years
        /// ago — before this server existed, or on another device — is watched
        /// as far as Simkl is concerned even though Jellyfin has never seen it.
        /// </remarks>
        /// <param name="query">The item to look up.</param>
        /// <param name="userToken">User token.</param>
        /// <returns><c>true</c> or <c>false</c>, or null when Simkl couldn't answer.</returns>
        public async Task<bool?> IsWatchedAsync(SimklWatchedQuery query, string userToken)
        {
            // For an episode the title-level answer is useless: it is true for
            // every episode of a show the user is watching. Only the per-episode
            // breakdown, which "extended=episodes" adds, tells the truth.
            var isEpisode = query.Season != null && query.Episode != null;

            using var options = GetOptions(userToken);
            options.RequestUri = BuildUri(isEpisode ? "/sync/watched?extended=episodes" : "/sync/watched");
            options.Method = HttpMethod.Post;
            options.Content = new StringContent(
                JsonSerializer.Serialize(new[] { query }, _jsonSerializerOptions),
                Encoding.UTF8,
                MediaTypeNames.Application.Json);

            var response = await SendThrottledAsync(options, userToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Watched lookup returned {Status}", response.StatusCode);
                return null;
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Array
                    || document.RootElement.GetArrayLength() == 0)
                {
                    return null;
                }

                var first = document.RootElement[0];

                // "result" is the string "not_found" when Simkl couldn't resolve
                // the ids at all, which is not an answer either way.
                if (first.TryGetProperty("result", out var result)
                    && result.ValueKind == JsonValueKind.String)
                {
                    return null;
                }

                return isEpisode
                    ? ReadEpisodeWatched(first, query.Season!.Value, query.Episode!.Value)
                    : ReadTitleWatched(first);
            }
            catch (JsonException ex)
            {
                _logger.LogDebug(ex, "Could not read the watched lookup response");
                return null;
            }
        }

        /// <summary>
        /// Reads the watched flag of one episode out of the season breakdown.
        /// </summary>
        private static bool? ReadEpisodeWatched(JsonElement item, int season, int episode)
        {
            if (!item.TryGetProperty("seasons", out var seasons)
                || seasons.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var seasonElement in seasons.EnumerateArray())
            {
                if (!seasonElement.TryGetProperty("number", out var seasonNumber)
                    || seasonNumber.ValueKind != JsonValueKind.Number
                    || seasonNumber.GetInt32() != season
                    || !seasonElement.TryGetProperty("episodes", out var episodes)
                    || episodes.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var episodeElement in episodes.EnumerateArray())
                {
                    if (episodeElement.TryGetProperty("number", out var episodeNumber)
                        && episodeNumber.ValueKind == JsonValueKind.Number
                        && episodeNumber.GetInt32() == episode
                        && episodeElement.TryGetProperty("watched", out var watched))
                    {
                        return watched.ValueKind == JsonValueKind.True;
                    }
                }
            }

            // The episode isn't in the breakdown at all: no answer rather than
            // a guess, so the caller treats it as a first watch.
            return null;
        }

        /// <summary>
        /// Reads whether a movie has been finished. Only "completed" counts: an
        /// item merely sitting on the watchlist has not been watched.
        /// </summary>
        private static bool? ReadTitleWatched(JsonElement item)
        {
            if (item.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.String)
            {
                return string.Equals(list.GetString(), "completed", StringComparison.OrdinalIgnoreCase);
            }

            return null;
        }

        /// <summary>
        /// Records a watch of something the user already finished, as a Simkl
        /// rewatch session (<c>POST /sync/history?allow_rewatch=yes</c>).
        /// </summary>
        /// <remarks>
        /// Simkl decides whether to open a new session or extend the running
        /// one; the id it returns must be pinned on later writes so a series
        /// rewatched episode by episode stays in a single session. Rewatches
        /// are a Pro/VIP feature: for a free account the call is accepted and
        /// silently does nothing.
        /// </remarks>
        /// <param name="history">The watch to record.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>The rewatch session id, when Simkl reported one.</returns>
        public async Task<RewatchSession?> AddRewatchAsync(SimklHistory history, string userToken)
        {
            using var options = GetOptions(userToken);
            options.RequestUri = BuildUri("/sync/history?allow_rewatch=yes");
            options.Method = HttpMethod.Post;
            options.Content = new StringContent(
                JsonSerializer.Serialize(history, _jsonSerializerOptions),
                Encoding.UTF8,
                MediaTypeNames.Application.Json);

            var response = await SendThrottledAsync(options, userToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token");
            }

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Rewatch write returned {Status}", response.StatusCode);
                return null;
            }

            // Read the id defensively: only this one field matters, and the
            // surrounding shape is free to grow.
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("added", out var added)
                    && added.TryGetProperty("statuses", out var statuses)
                    && statuses.ValueKind == JsonValueKind.Array)
                {
                    foreach (var status in statuses.EnumerateArray())
                    {
                        // Simkl nests the id under "response"; the flat form is
                        // kept for safety in case the shape ever changes.
                        var holder = status;
                        if (!TryReadRewatchId(holder, out var value)
                            && status.TryGetProperty("response", out var inner))
                        {
                            holder = inner;
                            TryReadRewatchId(holder, out value);
                        }

                        if (value != 0)
                        {
                            var state = holder.TryGetProperty("rewatch_status", out var st)
                                        && st.ValueKind == JsonValueKind.String
                                ? st.GetString()
                                : null;
                            _logger.LogInformation("Simkl rewatch session {Session} is {State}", value, state ?? "open");
                            return new RewatchSession(value, state);
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Could not read the rewatch id from the response");
            }

            // The body carries only counters and ids, never credentials.
            _logger.LogInformation(
                "Rewatch write accepted without a rewatch session; Simkl answered: {Body}",
                body.Length > 400 ? body.Substring(0, 400) + "..." : body);
            return null;
        }

        private static bool TryReadRewatchId(JsonElement element, out int value)
        {
            value = 0;
            return element.ValueKind == JsonValueKind.Object
                   && element.TryGetProperty("rewatch_id", out var id)
                   && id.ValueKind == JsonValueKind.Number
                   && id.TryGetInt32(out value);
        }

        /// <summary>
        /// Removes the given items from the user's Simkl watch history
        /// (<c>POST /sync/history/remove</c>).
        /// </summary>
        /// <param name="history">History object.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>The sync history response.</returns>
        public async Task<SyncHistoryResponse?> RemoveFromHistory(SimklHistory history, string userToken)
        {
            try
            {
                return await Post<SyncHistoryResponse, SimklHistory>("/sync/history/remove", userToken, history)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError(e, "Invalid user token, deleting");
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token");
            }
        }

        /// <summary>
        /// Fetches the user's Simkl watch statistics
        /// (<c>POST /users/{id}/stats</c>) and returns the raw JSON body, so
        /// callers can pass it through without depending on the exact response
        /// shape. The Simkl account id is resolved through the settings call.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <returns>The raw JSON response, or null when the request failed.</returns>
        /// <remarks>
        /// The stats call opens the user's whole history on Simkl's side, so the
        /// result is kept until the activity endpoint reports that anything
        /// changed for the user. The stats only appear on the settings page.
        /// </remarks>
        public async Task<string?> GetUserStatsRaw(string userToken)
        {
            _statsCache.TryGetValue(userToken, out var cached);
            var activity = await GetActivityAsync(userToken).ConfigureAwait(false);
            if (activity.Unauthorized)
            {
                DropToken(userToken);
                return null;
            }

            if (cached != null
                && (activity.AllStamp == null
                    || string.Equals(activity.AllStamp, cached.Stamp, StringComparison.Ordinal)))
            {
                return cached.Raw;
            }

            // Settings are activity-gated too, so this is normally free.
            var settings = await GetUserSettings(userToken).ConfigureAwait(false);
            var accountId = settings?.Account?.Id;
            if (accountId == null)
            {
                _logger.LogDebug("No Simkl account id available, can't fetch stats");
                return cached?.Raw;
            }

            using var options = GetOptions(userToken);
            options.RequestUri = BuildUri("/users/" + accountId.Value.ToString(CultureInfo.InvariantCulture) + "/stats");
            options.Method = HttpMethod.Post;
            var response = await SendThrottledAsync(options, userToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("POST /users/{{id}}/stats returned {Status}", response.StatusCode);
                return cached?.Raw;
            }

            var raw = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            _statsCache[userToken] = new CachedStats(raw, activity.AllStamp);
            return raw;
        }

        /// <summary>
        /// A scrobble call is considered successful when the server accepted it
        /// (2xx) or reported a duplicate completion (<c>409</c>), which means the
        /// item is already watched and no retry is needed.
        /// </summary>
        private static bool IsScrobbleSuccess(System.Net.HttpStatusCode status)
        {
            return ((int)status >= 200 && (int)status < 300)
                   || status == System.Net.HttpStatusCode.Conflict;
        }

        private static SimklScrobbleBody? BuildScrobbleBody(BaseItemDto item, double progress, Dictionary<string, string>? seriesProviderIds = null)
        {
            var body = new SimklScrobbleBody { Progress = ClampProgress(progress) };

            if (item.IsMovie == true || item.Type == BaseItemKind.Movie)
            {
                body.Movie = new ScrobbleMovie(item);
            }
            else if (item.Type == BaseItemKind.Episode
                     || item.IsSeries == true
                     || item.Type == BaseItemKind.Series)
            {
                // Use the resolved series provider ids when available, otherwise
                // fall back to the item's own ids (which for an episode are the
                // episode-level ids — they may or may not match the show on Simkl).
                if (seriesProviderIds != null)
                {
                    body.Show = new ScrobbleShow
                    {
                        Title = item.SeriesName,
                        Year = item.ProductionYear,
                        Ids = new SimklShowIds(seriesProviderIds)
                    };
                }
                else
                {
                    body.Show = new ScrobbleShow(item);
                }

                body.Episode = new ScrobbleEpisode(item);
            }
            else
            {
                return null;
            }

            return body;
        }

        private static double ClampProgress(double progress)
        {
            return Math.Round(Math.Clamp(progress, 0d, 100d), 2);
        }

        /// <summary>
        /// Posts a scrobble body and returns the HTTP status code.
        /// </summary>
        private async Task<System.Net.HttpStatusCode> PostScrobble(SimklScrobbleAction action, SimklScrobbleBody body, string userToken)
        {
            var endpoint = "/scrobble/" + action.ToString().ToLowerInvariant();
            using var options = GetOptions(userToken);
            options.RequestUri = BuildUri(endpoint);
            options.Method = HttpMethod.Post;
            options.Content = new StringContent(
                JsonSerializer.Serialize(body, _jsonSerializerOptions),
                Encoding.UTF8,
                MediaTypeNames.Application.Json);

            _logger.LogDebug("POST {Endpoint} {@Body}", endpoint, body);

            var response = await SendThrottledAsync(options, userToken).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError("Invalid user token, deleting");
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token");
            }

            return response.StatusCode;
        }

        /// <summary>
        /// Builds a scrobble body by resolving the item through Simkl's filename
        /// search, used when the provider ids alone don't resolve.
        /// </summary>
        private async Task<SimklScrobbleBody?> BuildScrobbleBodyFromFile(BaseItemDto item, double progress, bool fullpath)
        {
            var fname = fullpath ? item.Path : Path.GetFileName(item.Path);
            var mo = await GetFromFile(fname).ConfigureAwait(false);
            if (mo == null)
            {
                throw new InvalidDataException("Search file response is null");
            }

            var body = new SimklScrobbleBody { Progress = ClampProgress(progress) };

            if (mo.Movie != null && (item.IsMovie == true || item.Type == BaseItemKind.Movie))
            {
                if (!string.Equals(mo.Type, "movie", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != movie (" + mo.Type + ")");
                }

                body.Movie = new ScrobbleMovie
                {
                    Title = mo.Movie.Title,
                    Year = mo.Movie.Year,
                    Ids = mo.Movie.Ids
                };
            }
            else if (mo.Episode != null
                     && mo.Show != null
                     && (item.IsSeries == true || item.Type == BaseItemKind.Episode))
            {
                if (!string.Equals(mo.Type, "episode", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != episode (" + mo.Type + ")");
                }

                body.Show = new ScrobbleShow
                {
                    Title = mo.Show.Title,
                    Year = mo.Show.Year,
                    Ids = mo.Show.Ids
                };
                body.Episode = new ScrobbleEpisode
                {
                    Season = mo.Episode.Season,
                    Number = mo.Episode.Episode
                };
            }
            else
            {
                return null;
            }

            return body;
        }

        /// <summary>
        /// Get from file.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <returns>Search file response.</returns>
        private async Task<SearchFileResponse?> GetFromFile(string filename)
        {
            var f = new SimklFile { File = filename };
            _logger.LogInformation("Posting: {@File}", f);
            return await Post<SearchFileResponse, SimklFile>("/search/file/", null, f);
        }

        /// <summary>
        /// Get history from file name.
        /// </summary>
        /// <param name="item">Item.</param>
        /// <param name="fullpath">Full path.</param>
        /// <returns>Srobble history.</returns>
        private async Task<(SimklHistory history, BaseItemDto item)> GetHistoryFromFileName(BaseItemDto item, bool fullpath = true)
        {
            var fname = fullpath ? item.Path : Path.GetFileName(item.Path);
            var mo = await GetFromFile(fname);
            if (mo == null)
            {
                throw new InvalidDataException("Search file response is null");
            }

            var history = new SimklHistory();
            if (mo.Movie != null &&
                (item.IsMovie == true || item.Type == BaseItemKind.Movie))
            {
                if (!string.Equals(mo.Type, "movie", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != movie (" + mo.Type + ")");
                }

                item.Name = mo.Movie.Title;
                item.ProductionYear = mo.Movie.Year;
                history.Movies.Add(mo.Movie);
            }
            else if (mo.Episode != null
                     && mo.Show != null
                     && (item.IsSeries == true || item.Type == BaseItemKind.Episode))
            {
                if (!string.Equals(mo.Type, "episode", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("type != episode (" + mo.Type + ")");
                }

                item.Name = mo.Episode.Title;
                item.SeriesName = mo.Show.Title;
                item.IndexNumber = mo.Episode.Episode;
                item.ParentIndexNumber = mo.Episode.Season;
                item.ProductionYear = mo.Show.Year;
                history.Episodes.Add(mo.Episode);
            }

            return (history, item);
        }

        private static HttpRequestMessage GetOptions(string? userToken = null)
        {
            var requestMessage = new HttpRequestMessage();
            requestMessage.Headers.TryAddWithoutValidation("simkl-api-key", Apikey);
            requestMessage.Headers.UserAgent.Add(new ProductInfoHeaderValue("SimklScrobbler-Jellyfin", _appVersion));
            if (!string.IsNullOrEmpty(userToken))
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            }

            return requestMessage;
        }

        /// <summary>
        /// Builds the absolute request URI, appending the app identification
        /// parameters (client_id, app-name, app-version) Simkl requires on
        /// every request.
        /// </summary>
        private static Uri BuildUri(string relativeUrl)
        {
            if (relativeUrl.Contains('?', StringComparison.Ordinal))
            {
                return new Uri(Baseurl + relativeUrl + '&' + _identityQuery);
            }

            // The Simkl router doesn't resolve "path/?query": a trailing slash
            // followed by a query string yields a 200 with a null body.
            return new Uri(Baseurl + relativeUrl.TrimEnd('/') + '?' + _identityQuery);
        }

        private static SimklHistory CreateHistoryFromItem(BaseItemDto item)
        {
            var history = new SimklHistory();

            if (item.IsMovie == true || item.Type == BaseItemKind.Movie)
            {
                history.Movies.Add(new SimklMovie(item));
            }
            else if (item.IsSeries == true || (item.Type == BaseItemKind.Series))
            {
                // Jellyfin sends episode id instead of show id
                // TODO: TV Shows scrobbling (WIP)
                history.Shows.Add(new SimklShow(item));
            }
            else if (item.Type == BaseItemKind.Episode)
            {
                history.Episodes.Add(new SimklEpisode(item));
            }

            return history;
        }

        /// <summary>
        /// Implements /sync/history method from simkl.
        /// </summary>
        /// <param name="history">History object.</param>
        /// <param name="userToken">User token.</param>
        /// <returns>The sync history response.</returns>
        private async Task<SyncHistoryResponse?> SyncHistoryAsync(SimklHistory history, string userToken)
        {
            try
            {
                _logger.LogInformation("Syncing History");
                return await Post<SyncHistoryResponse, SimklHistory>("/sync/history", userToken, history);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _logger.LogError(e, "Invalid user token, deleting");
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token");
            }
        }

        /// <summary>
        /// API's private get method, given RELATIVE url and headers.
        /// </summary>
        /// <param name="url">Relative url.</param>
        /// <param name="userToken">Authentication token.</param>
        /// <returns>HTTP(s) Stream to be used.</returns>
        private async Task<T?> Get<T>(string url, string? userToken = null)
        {
            // Todo: If string is not null neither empty
            using var options = GetOptions(userToken);
            options.RequestUri = BuildUri(url);
            options.Method = HttpMethod.Get;
            var responseMessage = await SendThrottledAsync(options, userToken);
            return await responseMessage.Content.ReadFromJsonAsync<T>(_jsonSerializerOptions);
        }

        /// <summary>
        /// API's private post method.
        /// </summary>
        /// <param name="url">Relative post url.</param>
        /// <param name="userToken">Authentication token.</param>
        /// <param name="data">Object to serialize.</param>
        private async Task<T1?> Post<T1, T2>(string url, string? userToken = null, T2? data = null)
         where T2 : class
        {
            using var options = GetOptions(userToken);
            options.RequestUri = BuildUri(url);
            options.Method = HttpMethod.Post;

            if (data != null)
            {
                options.Content = new StringContent(
                    JsonSerializer.Serialize(data, _jsonSerializerOptions),
                    Encoding.UTF8,
                    MediaTypeNames.Application.Json);
            }

            var responseMessage = await SendThrottledAsync(options, userToken);

            return await responseMessage.Content.ReadFromJsonAsync<T1>(_caseInsensitiveJsonSerializerOptions);
        }

        /// <summary>
        /// Sends a request, spacing authenticated POSTs so one user never sends
        /// more than one write per second, as Simkl's rate limits require.
        /// Reads and unauthenticated calls go straight through.
        /// </summary>
        private async Task<HttpResponseMessage> SendThrottledAsync(HttpRequestMessage request, string? userToken)
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            if (request.Method != HttpMethod.Post || string.IsNullOrEmpty(userToken))
            {
                return await client.SendAsync(request).ConfigureAwait(false);
            }

            var gate = _postGates.GetOrAdd(userToken, _ => new PostGate());
            await gate.Lock.WaitAsync().ConfigureAwait(false);
            try
            {
                var wait = _minPostInterval - (DateTime.UtcNow - gate.LastPostUtc);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait).ConfigureAwait(false);
                }

                try
                {
                    return await client.SendAsync(request).ConfigureAwait(false);
                }
                finally
                {
                    gate.LastPostUtc = DateTime.UtcNow;
                }
            }
            finally
            {
                gate.Lock.Release();
            }
        }

        /// <summary>
        /// Reads <c>GET /sync/activities</c>, the cheap way to know whether the
        /// user's settings or history changed. One reading is reused for a few
        /// minutes so a page load costs a single call.
        /// </summary>
        private async Task<ActivitySnapshot> GetActivityAsync(string userToken)
        {
            if (_activityCache.TryGetValue(userToken, out var memo)
                && DateTime.UtcNow - memo.CheckedUtc < _activityMemo)
            {
                return memo;
            }

            var snapshot = new ActivitySnapshot { CheckedUtc = DateTime.UtcNow };
            try
            {
                using var options = GetOptions(userToken);
                options.RequestUri = BuildUri("/sync/activities");
                options.Method = HttpMethod.Get;
                var response = await SendThrottledAsync(options, userToken).ConfigureAwait(false);
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    snapshot.Unauthorized = true;
                    return snapshot;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("GET /sync/activities returned {Status}", response.StatusCode);
                    return snapshot;
                }

                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var document = JsonDocument.Parse(body);
                var rootElement = document.RootElement;
                if (rootElement.ValueKind == JsonValueKind.Object)
                {
                    if (rootElement.TryGetProperty("all", out var all) && all.ValueKind == JsonValueKind.String)
                    {
                        snapshot.AllStamp = all.GetString();
                    }

                    if (rootElement.TryGetProperty("settings", out var settings)
                        && settings.ValueKind == JsonValueKind.Object
                        && settings.TryGetProperty("all", out var settingsAll)
                        && settingsAll.ValueKind == JsonValueKind.String)
                    {
                        snapshot.SettingsStamp = settingsAll.GetString();
                    }
                    else
                    {
                        // No settings activity yet (fresh account): key on the global stamp.
                        snapshot.SettingsStamp = snapshot.AllStamp == null ? null : "all:" + snapshot.AllStamp;
                    }
                }

                _activityCache[userToken] = snapshot;
                return snapshot;
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is JsonException || ex is TaskCanceledException)
            {
                _logger.LogDebug(ex, "Could not read the Simkl activity");
                return snapshot;
            }
        }

        private void DropToken(string userToken)
        {
            SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
            _settingsCache.TryRemove(userToken, out _);
            _statsCache.TryRemove(userToken, out _);
            _activityCache.TryRemove(userToken, out _);
        }

        private static CachedSettings? LoadStoredSettings(string userToken)
        {
            var configs = SimklPlugin.Instance?.Configuration.UserConfigs;
            if (configs == null)
            {
                return null;
            }

            foreach (var config in configs)
            {
                if (string.Equals(config.UserToken, userToken, StringComparison.Ordinal)
                    && config.SimklAccountId != null
                    && !string.IsNullOrEmpty(config.SettingsStamp))
                {
                    var settings = new UserSettings
                    {
                        User = new User { Name = config.SimklUserName },
                        Account = new SimklAccount { Id = config.SimklAccountId, Type = config.AccountType }
                    };
                    return new CachedSettings(settings, config.SettingsStamp);
                }
            }

            return null;
        }

        private static void StoreSettings(string userToken, CachedSettings entry)
        {
            var configs = SimklPlugin.Instance?.Configuration.UserConfigs;
            if (configs == null)
            {
                return;
            }

            var changed = false;
            foreach (var config in configs)
            {
                if (string.Equals(config.UserToken, userToken, StringComparison.Ordinal))
                {
                    config.SimklUserName = entry.Settings.User?.Name;
                    config.SimklAccountId = entry.Settings.Account?.Id;
                    config.AccountType = entry.Settings.Account?.Type;
                    config.AccountTypeCheckedUtc = DateTime.UtcNow;
                    config.SettingsStamp = entry.Stamp;
                    changed = true;
                }
            }

            if (changed)
            {
                SimklPlugin.Instance?.SaveConfiguration();
            }
        }

        /// <summary>
        /// One reading of the activity endpoint.
        /// </summary>
        private sealed class ActivitySnapshot
        {
            public bool Unauthorized { get; set; }

            public string? AllStamp { get; set; }

            public string? SettingsStamp { get; set; }

            public DateTime CheckedUtc { get; set; }
        }

        /// <summary>
        /// A user's settings snapshot and the activity stamp it was read under.
        /// </summary>
        private sealed class CachedSettings
        {
            public CachedSettings(UserSettings settings, string? stamp)
            {
                Settings = settings;
                Stamp = stamp;
            }

            public UserSettings Settings { get; }

            public string? Stamp { get; }
        }

        /// <summary>
        /// The watch statistics and the activity stamp they were read under.
        /// </summary>
        private sealed class CachedStats
        {
            public CachedStats(string raw, string? stamp)
            {
                Raw = raw;
                Stamp = stamp;
            }

            public string Raw { get; }

            public string? Stamp { get; }
        }

        /// <summary>
        /// Per-user write pacing: one write at a time, at least a second apart.
        /// </summary>
        private sealed class PostGate : IDisposable
        {
            /// <summary>
            /// Gets the lock serialising this user's writes.
            /// </summary>
            public SemaphoreSlim Lock { get; } = new SemaphoreSlim(1, 1);

            /// <summary>
            /// Gets or sets when this user's last write was sent.
            /// </summary>
            public DateTime LastPostUtc { get; set; } = DateTime.MinValue;

            /// <inheritdoc />
            public void Dispose()
            {
                Lock.Dispose();
            }
        }
    }
}
