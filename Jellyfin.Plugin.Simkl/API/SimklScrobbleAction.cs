namespace Jellyfin.Plugin.Simkl.API
{
    /// <summary>
    /// Simkl scrobble action.
    /// </summary>
    /// <remarks>
    /// Maps directly to the <c>/scrobble/{action}</c> endpoints described in the
    /// Simkl scrobble guide. The lowercased name is used as the URL segment.
    /// </remarks>
    public enum SimklScrobbleAction
    {
        /// <summary>
        /// Playback started or resumed (<c>POST /scrobble/start</c>).
        /// Creates an active session and shows the title in "Watching now".
        /// </summary>
        Start,

        /// <summary>
        /// Playback paused (<c>POST /scrobble/pause</c>).
        /// Saves the current progress as a resumable playback.
        /// </summary>
        Pause,

        /// <summary>
        /// Playback stopped or ended (<c>POST /scrobble/stop</c>).
        /// Progress &gt;= 80 marks the item watched, otherwise a paused playback is saved.
        /// </summary>
        Stop
    }
}
