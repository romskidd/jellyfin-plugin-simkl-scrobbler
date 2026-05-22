using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects.Scrobble
{
    /// <summary>
    /// Movie payload sent to the <c>/scrobble/*</c> endpoints.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="SimklMovie"/> (used for <c>/sync/history</c>) this object
    /// carries no <c>watched_at</c> timestamp — the scrobble lifecycle decides the
    /// watched state server-side from the reported progress.
    /// </remarks>
    public class ScrobbleMovie
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScrobbleMovie"/> class.
        /// </summary>
        public ScrobbleMovie()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScrobbleMovie"/> class.
        /// </summary>
        /// <param name="item">The base item dto.</param>
        public ScrobbleMovie(BaseItemDto item)
        {
            Title = item.Name ?? item.OriginalTitle;
            Year = item.ProductionYear;
            Ids = new SimklMovieIds(item.ProviderIds);
        }

        /// <summary>
        /// Gets or sets the movie title.
        /// </summary>
        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the production year.
        /// </summary>
        [JsonPropertyName("year")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the external ids.
        /// </summary>
        [JsonPropertyName("ids")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SimklIds? Ids { get; set; }
    }
}
