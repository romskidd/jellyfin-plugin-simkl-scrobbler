using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// A watch that Simkl never acknowledged, kept so it can be replayed later.
    /// </summary>
    /// <remarks>
    /// Stores the identity of the item rather than a ready-made request body, so
    /// the payload is rebuilt with the current code when the retry happens.
    /// </remarks>
    public class PendingScrobble
    {
        /// <summary>
        /// Gets or sets the Jellyfin user this watch belongs to.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the item is a movie.
        /// </summary>
        public bool IsMovie { get; set; }

        /// <summary>
        /// Gets or sets the item name, for logging.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the title sent to Simkl (the series title for episodes).
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the production year.
        /// </summary>
        public int? Year { get; set; }

        /// <summary>
        /// Gets or sets the provider ids (series-level ids for episodes).
        /// </summary>
        public Dictionary<string, string> ProviderIds { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets or sets the season number, for episodes.
        /// </summary>
        public int? Season { get; set; }

        /// <summary>
        /// Gets or sets the episode number, for episodes.
        /// </summary>
        public int? Episode { get; set; }

        /// <summary>
        /// Gets or sets when the watch first failed to reach Simkl.
        /// </summary>
        public DateTime FirstFailedUtc { get; set; }

        /// <summary>
        /// Gets or sets how many times the replay has been attempted.
        /// </summary>
        public int Attempts { get; set; }
    }
}
