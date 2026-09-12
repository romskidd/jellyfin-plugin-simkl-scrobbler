using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Removes the entry earlier versions registered with the Plugin Pages
    /// plugin, now that the plugin adds its own menu entry.
    /// </summary>
    /// <remarks>
    /// Up to 9.7.0.0 the self-service page was declared in Plugin Pages' own
    /// config file, when that plugin was installed. Left there, it would show
    /// next to the built-in entry as a duplicate. Only the entry with this
    /// plugin's id is removed; everything else in that file is left untouched,
    /// and nothing happens at all when Plugin Pages is not installed.
    /// </remarks>
    public class PluginPagesCleanup : IHostedService
    {
        private const string EntryId = "Jellyfin.Plugin.Simkl";

        private static readonly Guid _pluginPagesId = new Guid("5b6550fa-a014-4f4c-8a2c-59a43680ac6d");

        private readonly IPluginManager _pluginManager;
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<PluginPagesCleanup> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginPagesCleanup"/> class.
        /// </summary>
        /// <param name="pluginManager">Instance of the <see cref="IPluginManager"/> interface.</param>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{PluginPagesCleanup}"/> interface.</param>
        public PluginPagesCleanup(
            IPluginManager pluginManager,
            IApplicationPaths applicationPaths,
            ILogger<PluginPagesCleanup> logger)
        {
            _pluginManager = pluginManager;
            _applicationPaths = applicationPaths;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                Cleanup();
            }
            catch (Exception ex)
            {
                // A leftover duplicate entry is not worth failing the plugin over.
                _logger.LogDebug(ex, "Could not remove the old Plugin Pages entry");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void Cleanup()
        {
            if (!_pluginManager.Plugins.Any(p => p.Id == _pluginPagesId))
            {
                return;
            }

            var configPath = Path.Combine(_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.PluginPages", "config.json");
            if (!File.Exists(configPath))
            {
                return;
            }

            if (JsonNode.Parse(File.ReadAllText(configPath)) is not JsonObject root)
            {
                return;
            }

            var key = root.ContainsKey("pages") ? "pages" : "Pages";
            if (root[key] is not JsonArray pages)
            {
                return;
            }

            var removed = false;
            for (var i = pages.Count - 1; i >= 0; i--)
            {
                if (pages[i] is JsonObject page
                    && string.Equals(page["Id"]?.GetValue<string>(), EntryId, StringComparison.Ordinal))
                {
                    pages.RemoveAt(i);
                    removed = true;
                }
            }

            if (!removed)
            {
                return;
            }

            File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _logger.LogInformation("Removed the old Plugin Pages entry: the plugin now adds its own menu entry");
        }
    }
}
