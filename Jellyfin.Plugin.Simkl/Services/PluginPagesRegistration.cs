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
    /// Optional integration: when the Plugin Pages plugin is installed, adds the
    /// self-service page to the user-facing sidebar it provides.
    /// </summary>
    /// <remarks>
    /// Plugin Pages has no registration API yet, so pages are declared by adding
    /// an entry to its own config file — the mechanism documented in its README.
    /// When it isn't installed this does nothing at all: the self-service page
    /// stays reachable through its direct link, and the plugin never depends on
    /// Plugin Pages (nor on File Transformation, which Plugin Pages requires).
    /// </remarks>
    public class PluginPagesRegistration : IHostedService
    {
        private const string EntryId = "Jellyfin.Plugin.Simkl";
        private const string PageUrl = "/Simkl/Link/Fragment";

        private static readonly Guid _pluginPagesId = new Guid("5b6550fa-a014-4f4c-8a2c-59a43680ac6d");

        private readonly IPluginManager _pluginManager;
        private readonly IApplicationPaths _applicationPaths;
        private readonly ILogger<PluginPagesRegistration> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginPagesRegistration"/> class.
        /// </summary>
        /// <param name="pluginManager">Instance of the <see cref="IPluginManager"/> interface.</param>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{PluginPagesRegistration}"/> interface.</param>
        public PluginPagesRegistration(
            IPluginManager pluginManager,
            IApplicationPaths applicationPaths,
            ILogger<PluginPagesRegistration> logger)
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
                Register();
            }
            catch (Exception ex)
            {
                // Never let an optional nicety stop the plugin from loading.
                _logger.LogDebug(ex, "Could not register the page with Plugin Pages");
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void Register()
        {
            if (!_pluginManager.Plugins.Any(p => p.Id == _pluginPagesId))
            {
                _logger.LogDebug("Plugin Pages is not installed, self-service page reachable by direct link only");
                return;
            }

            var directory = Path.Combine(_applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.PluginPages");
            var configPath = Path.Combine(directory, "config.json");

            JsonObject root;
            if (File.Exists(configPath))
            {
                root = JsonNode.Parse(File.ReadAllText(configPath)) as JsonObject ?? new JsonObject();
            }
            else
            {
                Directory.CreateDirectory(directory);
                root = new JsonObject();
            }

            // Keep whatever key casing the file already uses.
            var key = root.ContainsKey("pages") ? "pages" : "Pages";
            if (root[key] is not JsonArray pages)
            {
                pages = new JsonArray();
                root[key] = pages;
            }

            // Drop any previous entry of ours (url or label may have changed),
            // leaving every other plugin's entry untouched.
            for (var i = pages.Count - 1; i >= 0; i--)
            {
                if (pages[i] is JsonObject page
                    && string.Equals(page["Id"]?.GetValue<string>(), EntryId, StringComparison.Ordinal))
                {
                    pages.RemoveAt(i);
                }
            }

            pages.Add(new JsonObject
            {
                ["Id"] = EntryId,
                ["Url"] = PageUrl,
                ["DisplayText"] = "RK Simkl Scrobbler",
                ["Icon"] = "sync",
                ["Version"] = 1
            });

            File.WriteAllText(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _logger.LogInformation("Registered the Simkl self-service page with Plugin Pages");
        }
    }
}
