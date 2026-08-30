using System;
using System.Collections.Generic;
using Jellyfin.Plugin.Simkl.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Simkl
{
    /// <summary>
    /// SIMKL tracker.
    /// </summary>
    public class SimklPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SimklPlugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
        public SimklPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the current instance of the plugin.
        /// </summary>
        public static SimklPlugin? Instance { get; private set; }

        /// <inheritdoc />
        public override Guid Id => new Guid("07CAEF58-A94B-4211-A62C-F9774E04EBDB");

        /// <inheritdoc />
        public override string Name => "Simkl (moved to Simkl Scrobbler)";

        /// <inheritdoc />
        public override string Description => "THIS PLUGIN HAS MOVED: install 'Simkl Scrobbler' from the same repository to keep receiving updates, then uninstall this one. Your Simkl login and settings are kept. Scrobbling keeps working in the meantime.";

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            };
        }
    }
}