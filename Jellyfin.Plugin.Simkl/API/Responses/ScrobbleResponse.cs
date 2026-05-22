using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Responses
{
    /// <summary>
    /// Response returned by the <c>/scrobble/*</c> endpoints.
    /// </summary>
    public class ScrobbleResponse
    {
        /// <summary>
        /// Gets or sets the action Simkl recorded: <c>start</c>, <c>pause</c>,
        /// <c>scrobble</c> (watched) or <c>checkin</c>.
        /// </summary>
        [JsonPropertyName("action")]
        public string? Action { get; set; }

        /// <summary>
        /// Gets or sets the normalized progress echoed back by the server.
        /// </summary>
        [JsonPropertyName("progress")]
        public double? Progress { get; set; }
    }
}
