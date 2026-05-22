using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects.Scrobble
{
    /// <summary>
    /// Episode payload sent to the <c>/scrobble/*</c> endpoints.
    /// </summary>
    /// <remarks>
    /// Per the Simkl guide we send <c>season</c> + <c>number</c> (the Western-TV
    /// numbering scheme) which is stable forever; Simkl resolves it to the right
    /// canonical record, including anime cours, server-side.
    /// </remarks>
    public class ScrobbleEpisode
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScrobbleEpisode"/> class.
        /// </summary>
        public ScrobbleEpisode()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ScrobbleEpisode"/> class.
        /// </summary>
        /// <param name="item">The base item dto.</param>
        public ScrobbleEpisode(BaseItemDto item)
        {
            Season = item.ParentIndexNumber;
            Number = item.IndexNumber;
        }

        /// <summary>
        /// Gets or sets the season number.
        /// </summary>
        [JsonPropertyName("season")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode number within the season.
        /// </summary>
        [JsonPropertyName("number")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Number { get; set; }
    }
}
