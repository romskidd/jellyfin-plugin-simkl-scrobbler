using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects.Scrobble
{
    /// <summary>
    /// Show payload sent to the <c>/scrobble/*</c> endpoints alongside a
    /// <see cref="ScrobbleEpisode"/>.
    /// </summary>
    /// <remarks>
    /// The scrobble endpoints take the show-level ids plus a separate
    /// <c>episode</c> object with season + number, rather than the nested
    /// <c>seasons[]</c> structure used by <c>/sync/history</c>.
    /// </remarks>
    public class ScrobbleShow
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScrobbleShow"/> class.
        /// </summary>
        public ScrobbleShow()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScrobbleShow"/> class.
        /// </summary>
        /// <param name="item">The base item dto.</param>
        public ScrobbleShow(BaseItemDto item)
        {
            Title = item.SeriesName;
            Year = item.ProductionYear;
            Ids = new SimklShowIds(item.ProviderIds);
        }

        /// <summary>
        /// Gets or sets the show title.
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
