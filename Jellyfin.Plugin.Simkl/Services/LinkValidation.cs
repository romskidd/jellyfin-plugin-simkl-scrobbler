using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Simkl.API;
using Jellyfin.Plugin.Simkl.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Checks every stored Simkl link once after startup and drops the ones
    /// Simkl no longer accepts.
    /// </summary>
    /// <remarks>
    /// Tokens are bound to the Simkl application that issued them. A server
    /// migrating from the original Jellyfin plugin, or from a version of this
    /// one that still used its identity, carries tokens Simkl now rejects.
    /// Without this sweep the pages would show "Account connected" while
    /// nothing scrobbles, until the first playback finally fails.
    /// </remarks>
    public class LinkValidation : IHostedService, IDisposable
    {
        private static readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(45);

        private readonly SimklApi _simklApi;
        private readonly ILogger<LinkValidation> _logger;
        private CancellationTokenSource? _cts;

        /// <summary>
        /// Initializes a new instance of the <see cref="LinkValidation"/> class.
        /// </summary>
        /// <param name="simklApi">Instance of the <see cref="SimklApi"/>.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{LinkValidation}"/> interface.</param>
        public LinkValidation(SimklApi simklApi, ILogger<LinkValidation> logger)
        {
            _simklApi = simklApi;
            _logger = logger;
        }

        /// <inheritdoc />
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();
            _ = RunAsync(_cts.Token);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cts?.Cancel();
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _cts?.Dispose();
            GC.SuppressFinalize(this);
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Let the server finish starting before spending Simkl requests.
                await Task.Delay(_startupDelay, cancellationToken).ConfigureAwait(false);

                var configs = SimklPlugin.Instance?.Configuration.UserConfigs;
                if (configs == null)
                {
                    return;
                }

                // Snapshot: a rejected token is removed from the live config.
                var linked = new List<UserConfig>();
                foreach (var config in configs)
                {
                    if (!string.IsNullOrEmpty(config.UserToken))
                    {
                        linked.Add(config);
                    }
                }

                var dropped = 0;
                foreach (var config in linked)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // GetUserSettings drops the token itself when Simkl rejects it.
                    var settings = await _simklApi.GetUserSettings(config.UserToken).ConfigureAwait(false);
                    if (string.Equals(settings?.Error, "user_token_failed", StringComparison.Ordinal))
                    {
                        dropped++;
                        _logger.LogWarning(
                            "Simkl no longer accepts the link of Jellyfin user {UserId} (issued to another application, or revoked). They have to link again from the plugin page.",
                            config.Id);
                    }
                }

                _logger.LogInformation(
                    "Checked {Count} Simkl link(s) at startup, {Dropped} need linking again",
                    linked.Count,
                    dropped);
            }
            catch (OperationCanceledException)
            {
                // Server shutting down.
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not check the stored Simkl links");
            }
        }
    }
}
