using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Remembers the Simkl rewatch session opened for each show, so every
    /// episode of a rewatch is written into the same session.
    /// </summary>
    /// <remarks>
    /// Simkl warns that writes without the session id can fork silently and
    /// leave phantom duplicates, and a rewatch of a long series spans days and
    /// server restarts — so the ids are kept on disk rather than in memory.
    /// </remarks>
    public class RewatchTracker
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<RewatchTracker> _logger;
        private readonly object _lock = new object();

        private Dictionary<string, int>? _sessions;

        /// <summary>
        /// Initializes a new instance of the <see cref="RewatchTracker"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{RewatchTracker}"/> interface.</param>
        public RewatchTracker(IApplicationPaths applicationPaths, ILogger<RewatchTracker> logger)
        {
            _applicationPaths = applicationPaths;
            _logger = logger;
        }

        /// <summary>
        /// Gets the rewatch session id known for an item, if any.
        /// </summary>
        /// <param name="userId">The Jellyfin user.</param>
        /// <param name="itemKey">Series id for episodes, item id for movies.</param>
        /// <returns>The session id, or null when no session is open.</returns>
        public int? Get(Guid userId, string itemKey)
        {
            lock (_lock)
            {
                Load();
                return _sessions!.TryGetValue(BuildKey(userId, itemKey), out var id) ? id : null;
            }
        }

        /// <summary>
        /// Stores the rewatch session id Simkl returned for an item.
        /// </summary>
        /// <param name="userId">The Jellyfin user.</param>
        /// <param name="itemKey">Series id for episodes, item id for movies.</param>
        /// <param name="rewatchId">The session id.</param>
        public void Set(Guid userId, string itemKey, int rewatchId)
        {
            lock (_lock)
            {
                Load();
                _sessions![BuildKey(userId, itemKey)] = rewatchId;
                Save();
            }
        }

        private static string BuildKey(Guid userId, string itemKey)
        {
            return userId.ToString("N") + "|" + itemKey;
        }

        private string BuildPath()
        {
            return Path.Combine(_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.Simkl.rewatches.json");
        }

        private void Load()
        {
            if (_sessions != null)
            {
                return;
            }

            _sessions = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                var path = BuildPath();
                if (!File.Exists(path))
                {
                    return;
                }

                var stored = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path));
                if (stored != null)
                {
                    _sessions = new Dictionary<string, int>(stored, StringComparer.Ordinal);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not restore the rewatch sessions");
            }
        }

        private void Save()
        {
            try
            {
                File.WriteAllText(
                    BuildPath(),
                    JsonSerializer.Serialize(_sessions, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not persist the rewatch sessions");
            }
        }
    }
}
