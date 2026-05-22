using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects.Scrobble
{
    /// <summary>
    /// Request body for the <c>/scrobble/start</c>, <c>/scrobble/pause</c> and
    /// <c>/scrobble/stop</c> endpoints.
    /// </summary>
    /// <remarks>
    /// Exactly one of <see cref="Movie"/> or (<see cref="Show"/> + <see cref="Episode"/>)
    /// is populated. Null members are omitted from the JSON so a single body shape
    /// works for both movies and episodes.
    /// </remarks>
    public class SimklScrobbleBody
    {
        /// <summary>
        /// Gets or sets the playback progress as a percentage (0 to 100).
        /// </summary>
        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        /// <summary>
        /// Gets or sets the movie being scrobbled.
        /// </summary>
        [JsonPropertyName("movie")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScrobbleMovie? Movie { get; set; }

        /// <summary>
        /// Gets or sets the show being scrobbled.
        /// </summary>
        [JsonPropertyName("show")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScrobbleShow? Show { get; set; }

        /// <summary>
        /// Gets or sets the episode being scrobbled.
        /// </summary>
        [JsonPropertyName("episode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ScrobbleEpisode? Episode { get; set; }
    }
}
