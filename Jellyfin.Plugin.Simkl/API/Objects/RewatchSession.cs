using System;

namespace Jellyfin.Plugin.Simkl.API.Objects
{
    /// <summary>
    /// The rewatch session Simkl reports after a history write made with
    /// <c>allow_rewatch=yes</c>.
    /// </summary>
    public class RewatchSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RewatchSession"/> class.
        /// </summary>
        /// <param name="id">The session id.</param>
        /// <param name="status">The session state: active, closed or completed.</param>
        public RewatchSession(int id, string? status)
        {
            Id = id;
            Status = status;
        }

        /// <summary>
        /// Gets the session id, to pin on later writes.
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// Gets the session state reported by Simkl.
        /// </summary>
        public string? Status { get; }

        /// <summary>
        /// Gets a value indicating whether Simkl considers the session finished:
        /// every aired episode covered, or a movie (always complete at once).
        /// </summary>
        public bool IsCompleted => string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase);
    }
}
