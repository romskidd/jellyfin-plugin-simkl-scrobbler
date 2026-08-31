using System;
using System.Linq;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Decides whether an item belongs to a library the user excluded from
    /// scrobbling (home videos, kids' content, ...).
    /// </summary>
    /// <remarks>
    /// Membership is resolved from the item's path against each library's
    /// configured locations: that works for both playback events and manual
    /// check marks, without needing to walk the item's parents.
    /// </remarks>
    public class LibraryFilter
    {
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(5);

        private readonly ILibraryManager _libraryManager;
        private readonly object _lock = new object();

        private VirtualFolderInfo[] _cached = Array.Empty<VirtualFolderInfo>();
        private DateTime _cachedAt = DateTime.MinValue;

        /// <summary>
        /// Initializes a new instance of the <see cref="LibraryFilter"/> class.
        /// </summary>
        /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
        public LibraryFilter(ILibraryManager libraryManager)
        {
            _libraryManager = libraryManager;
        }

        /// <summary>
        /// Gets the server's libraries, cached briefly since they rarely change.
        /// </summary>
        /// <returns>The libraries.</returns>
        public VirtualFolderInfo[] GetLibraries()
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _cachedAt > _cacheDuration)
                {
                    _cached = _libraryManager.GetVirtualFolders().ToArray();
                    _cachedAt = DateTime.UtcNow;
                }

                return _cached;
            }
        }

        /// <summary>
        /// Checks whether the given media path sits in one of the libraries the
        /// user excluded.
        /// </summary>
        /// <param name="config">The user's config.</param>
        /// <param name="path">The media path.</param>
        /// <returns><c>true</c> when the item must not be scrobbled.</returns>
        public bool IsExcluded(UserConfig config, string? path)
        {
            var excluded = config.ExcludedLibraries;
            if (excluded == null || excluded.Length == 0 || string.IsNullOrEmpty(path))
            {
                return false;
            }

            foreach (var library in GetLibraries())
            {
                if (library.ItemId == null
                    || !excluded.Contains(library.ItemId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var location in library.Locations)
                {
                    if (!string.IsNullOrEmpty(location)
                        && path.StartsWith(location, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
