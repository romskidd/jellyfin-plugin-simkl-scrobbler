namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// The outcome of a scrobble call.
    /// </summary>
    public class ScrobbleResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether Simkl accepted the event
        /// (or reported it as already completed).
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the rewatch session Simkl filed the stop into, when the
        /// call was made with the rewatch flag and Simkl reported one.
        /// </summary>
        public RewatchSession? Rewatch { get; set; }
    }
}
