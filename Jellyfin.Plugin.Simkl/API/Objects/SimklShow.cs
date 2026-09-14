using System.Collections.Generic;
using System.Text.Json.Serialization;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// Simkl show.
    /// </summary>
    public class SimklShow : SimklMediaObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklShow"/> class.
        /// </summary>
        public SimklShow()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimklShow"/> class.
        /// </summary>
        /// <param name="mediaInfo">The media info.</param>
        public SimklShow(BaseItemDto mediaInfo)
        {
            Title = mediaInfo.SeriesName;
            Ids = new SimklShowIds(mediaInfo.ProviderIds);
            Year = mediaInfo.ProductionYear;
            Seasons = new[]
            {
                new Season
                {
                    Number = mediaInfo.ParentIndexNumber,
                    Episodes = new[]
                    {
                        new ShowEpisode
                        {
                            Number = mediaInfo.IndexNumber
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Gets or sets title.
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets year.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets seasons.
        /// </summary>
        [JsonPropertyName("seasons")]
        public IReadOnlyList<Season>? Seasons { get; set; }

        /// <summary>
        /// Gets or sets the rewatch session to write into. Pinning the id Simkl
        /// returned for the session keeps every episode of a rewatch in the same
        /// session instead of forking new ones.
        /// </summary>
        [JsonPropertyName("rewatch_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? RewatchId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Simkl reads the season and episode
        /// numbers as TVDB ones for an anime. Simkl files each anime season as a
        /// series of its own, while Jellyfin libraries number them like TVDB does;
        /// without this, a second season sent with the TVDB id lands on the wrong
        /// entry. Simkl ignores it for anything that is not an anime.
        /// </summary>
        [JsonPropertyName("use_tvdb_anime_seasons")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? UseTvdbAnimeSeasons { get; set; }
    }
}