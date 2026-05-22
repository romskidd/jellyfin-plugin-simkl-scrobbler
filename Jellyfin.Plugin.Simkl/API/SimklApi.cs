using System;
using System.Diagnostics.Tracing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
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
        public const string RedirectUri = @"https://simkl.com/apps/jellyfin/connected/";

        /// <summary>
        /// Api key.
        /// </summary>
        public const string Apikey = @"c721b22482097722a84a20ccc579cf9d232be85b9befe7b7805484d0ddbc6781";

        /// <summary>
        /// Secret.
        /// </summary>
        public const string Secret = @"87893fc73cdbd2e51a7c63975c6f941ac1c6155c0e20ffa76b83202dd10a507e";

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
            var uri = $"/oauth/pin?client_id={Apikey}&redirect={RedirectUri}";
            return await Get<CodeResponse>(uri);
        }

        /// <summary>
        /// Get code status.
        /// </summary>
        /// <param name="userCode">User code.</param>
        /// <returns>Code status.</returns>
        public async Task<CodeStatusResponse?> GetCodeStatus(string userCode)
        {
            var uri = $"/oauth/pin/{userCode}?client_id={Apikey}";
            return await Get<CodeStatusResponse>(uri);
        }

        /// <summary>
        /// Get user settings.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <returns>User settings.</returns>
        public async Task<UserSettings?> GetUserSettings(string userToken)
        {
            try
            {
                return await Post<UserSettings, object>("/users/settings/", userToken);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
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
        /// <returns><c>true</c> if Simkl accepted the event.</returns>
        public async Task<bool> ScrobbleAsync(SimklScrobbleAction action, BaseItemDto item, double progress, string userToken)
        {
            var body = BuildScrobbleBody(item, progress);
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
        /// A scrobble call is considered successful when the server accepted it
        /// (2xx) or reported a duplicate completion (<c>409</c>), which means the
        /// item is already watched and no retry is needed.
        /// </summary>
        private static bool IsScrobbleSuccess(System.Net.HttpStatusCode status)
        {
            return ((int)status >= 200 && (int)status < 300)
                   || status == System.Net.HttpStatusCode.Conflict;
        }

        private static SimklScrobbleBody? BuildScrobbleBody(BaseItemDto item, double progress)
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
                body.Show = new ScrobbleShow(item);
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
            options.RequestUri = new Uri(Baseurl + endpoint);
            options.Method = HttpMethod.Post;
            options.Content = new StringContent(
                JsonSerializer.Serialize(body, _jsonSerializerOptions),
                Encoding.UTF8,
                MediaTypeNames.Application.Json);

            _logger.LogDebug("POST {Endpoint} {@Body}", endpoint, body);

            var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(options).ConfigureAwait(false);

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
            if (!string.IsNullOrEmpty(userToken))
            {
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
            }

            return requestMessage;
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
                _logger.LogError(e, "Invalid user token {UserToken}, deleting", userToken);
                SimklPlugin.Instance?.Configuration.DeleteUserToken(userToken);
                throw new InvalidTokenException("Invalid user token " + userToken);
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
            options.RequestUri = new Uri(Baseurl + url);
            options.Method = HttpMethod.Get;
            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(options);
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
            options.RequestUri = new Uri(Baseurl + url);
            options.Method = HttpMethod.Post;

            if (data != null)
            {
                options.Content = new StringContent(
                    JsonSerializer.Serialize(data, _jsonSerializerOptions),
                    Encoding.UTF8,
                    MediaTypeNames.Application.Json);
            }

            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(options);

            return await responseMessage.Content.ReadFromJsonAsync<T1>(_caseInsensitiveJsonSerializerOptions);
        }
    }
}
