using System;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// The activity stamps that drive the import.
    /// </summary>
    public class ImportActivity
    {
        /// <summary>
        /// Gets or sets a value indicating whether Simkl rejected the token.
        /// </summary>
        public bool Unauthorized { get; set; }

        /// <summary>
        /// Gets or sets the tv_shows.all stamp.
        /// </summary>
        public string? ShowsStamp { get; set; }

        /// <summary>
        /// Gets or sets the movies.all stamp.
        /// </summary>
        public string? MoviesStamp { get; set; }

        /// <summary>
        /// Gets or sets the anime.all stamp.
        /// </summary>
        public string? AnimeStamp { get; set; }

        /// <summary>
        /// Gets or sets the playback stamps of shows, anime and movies, joined:
        /// they move whenever a paused playback is saved or cleared on Simkl.
        /// </summary>
        public string? PlaybackStamp { get; set; }
    }
}
