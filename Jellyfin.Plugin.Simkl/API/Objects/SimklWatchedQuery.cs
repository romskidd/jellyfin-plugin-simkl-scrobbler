using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// One item to look up through <c>POST /sync/watched</c>.
    /// </summary>
    /// <remarks>
    /// Unlike the sync payloads, this endpoint takes the external ids flat on
    /// the item rather than nested under <c>ids</c>. Pairing season and episode
    /// makes the answer about that episode instead of the whole show.
    /// </remarks>
    public class SimklWatchedQuery
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklWatchedQuery"/> class.
        /// </summary>
        /// <param name="providerIds">The provider ids of the item (series-level for episodes).</param>
        /// <param name="type">The Simkl item type: <c>show</c>, <c>movie</c> or <c>anime</c>.</param>
        public SimklWatchedQuery(Dictionary<string, string> providerIds, string type)
        {
            Type = type;
            foreach (var (key, value) in providerIds)
            {
                if (key.Equals("imdb", StringComparison.OrdinalIgnoreCase))
                {
                    Imdb = value;
                }
                else if (key.Equals("tmdb", StringComparison.OrdinalIgnoreCase))
                {
                    Tmdb = value;
                }
                else if (key.Equals("tvdb", StringComparison.OrdinalIgnoreCase))
                {
                    Tvdb = value;
                }
                else if (key.Equals("simkl", StringComparison.OrdinalIgnoreCase)
                         && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var simkl))
                {
                    Simkl = simkl;
                }
            }
        }

        /// <summary>
        /// Gets or sets the item type.
        /// </summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Type { get; set; }

        /// <summary>
        /// Gets or sets the imdb id.
        /// </summary>
        [JsonPropertyName("imdb")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Imdb { get; set; }

        /// <summary>
        /// Gets or sets the TMDb id.
        /// </summary>
        [JsonPropertyName("tmdb")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Tmdb { get; set; }

        /// <summary>
        /// Gets or sets the TVDB id.
        /// </summary>
        [JsonPropertyName("tvdb")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Tvdb { get; set; }

        /// <summary>
        /// Gets or sets the Simkl id.
        /// </summary>
        [JsonPropertyName("simkl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Simkl { get; set; }

        /// <summary>
        /// Gets or sets the season number, for an episode-level lookup.
        /// </summary>
        [JsonPropertyName("season")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode number, for an episode-level lookup.
        /// </summary>
        [JsonPropertyName("episode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Episode { get; set; }
    }
}
