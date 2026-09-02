using System;

namespace Jellyfin.Plugin.Simkl.Configuration
{
    /// <summary>
    /// User config.
    /// </summary>
    public class UserConfig
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UserConfig"/> class.
        /// </summary>
        public UserConfig()
        {
            ScrobbleMovies = true;
            ScrobbleShows = true;
            EnablePlaybackScrobbling = true;
            ScrobblePercentage = 70;
            ScrobbleNowWatchingPercentage = 5;
            MinLength = 5;
            UserToken = string.Empty; // Todo: check if token is still valid
            ScrobbleTimeout = 30;
            SyncMarkPlayed = true;
            SyncMarkUnplayed = false;
            ExcludedLibraries = Array.Empty<string>();
        }

        /// <summary>
        /// Gets or sets a value indicating whether scrobble movies.
        /// </summary>
        public bool ScrobbleMovies { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether real-time playback scrobbling
        /// (start / pause / stop, with the live "Watching now" banner) is enabled.
        /// When disabled the plugin sends no scrobble events at all.
        /// </summary>
        public bool EnablePlaybackScrobbling { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether scrobble shows.
        /// </summary>
        public bool ScrobbleShows { get; set; }

        /// <summary>
        /// Gets or sets scrobble percentage.
        /// </summary>
        public int ScrobblePercentage { get; set; }

        /// <summary>
        /// Gets or sets scrobble now watching percentage.
        /// </summary>
        public int ScrobbleNowWatchingPercentage { get; set; }

        /// <summary>
        /// Gets or sets min length.
        /// </summary>
        /// <remarks>
        /// Minimum length for scrobbling (in minutes).
        /// </remarks>
        public int MinLength { get; set; }

        /// <summary>
        /// Gets or sets user token.
        /// </summary>
        public string UserToken { get; set; } // Is the user logged in

        /// <summary>
        /// Gets or sets scrobble timeout.
        /// </summary>
        /// <remarks>
        /// Time between scrobbling tries.
        /// </remarks>
        public int ScrobbleTimeout { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether manually marking an item as
        /// played in Jellyfin adds it to the Simkl watch history.
        /// </summary>
        public bool SyncMarkPlayed { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether manually unmarking an item
        /// in Jellyfin also removes it from the Simkl watch history. Off by
        /// default to avoid accidental deletions.
        /// </summary>
        public bool SyncMarkUnplayed { get; set; }

        /// <summary>
        /// Gets or sets a short human-readable summary of the last scrobble
        /// attempt, shown on the configuration page.
        /// </summary>
        public string? LastScrobble { get; set; }

        /// <summary>
        /// Gets or sets a link to the last scrobbled item on Simkl, built on
        /// Simkl's redirect endpoint. Carries ids only, never credentials.
        /// </summary>
        public string? LastScrobbleUrl { get; set; }

        /// <summary>
        /// Gets or sets a short summary of the last rewatch recorded on Simkl.
        /// </summary>
        public string? LastRewatch { get; set; }

        /// <summary>
        /// Gets or sets the Simkl plan ("free", "pro" or "vip") as last reported
        /// by Simkl. Rewatch writes are only sent for paid plans, as Simkl asks.
        /// </summary>
        public string? AccountType { get; set; }

        /// <summary>
        /// Gets or sets when <see cref="AccountType"/> was last refreshed.
        /// </summary>
        public DateTime? AccountTypeCheckedUtc { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether finishing something already
        /// watched is recorded as a Simkl rewatch session. Simkl only honours
        /// this for Pro and VIP accounts.
        /// </summary>
        public bool EnableRewatches { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the Simkl token was rejected
        /// and removed. The user has to link again; without this the removal is
        /// silent and scrobbling appears to work while it no longer does.
        /// </summary>
        public bool LinkExpired { get; set; }

        /// <summary>
        /// Gets or sets the ids of the Jellyfin libraries that are never scrobbled.
        /// </summary>
        public string[] ExcludedLibraries { get; set; }

        /// <summary>
        /// Gets or sets user id.
        /// </summary>
        public Guid Id { get; set; }
    }
}