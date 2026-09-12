using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Simkl.Services
{
    /// <summary>
    /// Adds the plugin's menu script to the web client's index.html as it is
    /// served, so every user gets an "RK Simkl Scrobbler" entry in their menu
    /// with nothing else to install.
    /// </summary>
    /// <remarks>
    /// The web client is a static file with no hook for plugins, on 10.11 as on
    /// 12. Rather than rewriting that file on disk (which needs a writable web
    /// folder and is undone by every web client update), this middleware sits
    /// ahead of the static file handler and appends one script tag to the
    /// response, in memory, on each request. It only ever touches index.html,
    /// does nothing when the tag is already there, serves the original response
    /// untouched on any error, and is switched off by the "Show in the user
    /// menu" setting.
    /// </remarks>
    public class WebClientInjection : IStartupFilter
    {
        /// <summary>
        /// The path, under the server root, of the script this injects.
        /// </summary>
        public const string ScriptPath = "/Simkl/Client/menu.js";

        private readonly ILogger<WebClientInjection> _logger;
        private int _announced;

        /// <summary>
        /// Initializes a new instance of the <see cref="WebClientInjection"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{WebClientInjection}"/> interface.</param>
        public WebClientInjection(ILogger<WebClientInjection> logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                // Registered ahead of everything else, so that dropping
                // Accept-Encoding below reliably yields a plain response.
                app.Use(InvokeAsync);
                next(app);
            };
        }

        // The web app shell however it is requested: "/web", "/web/" and
        // "/web/index.html", with or without a base url in front.
        private static bool IsIndexRequest(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/web", StringComparison.OrdinalIgnoreCase);
        }

        // An absolute path, base url included: a relative one breaks when the
        // client is requested as "/web" without a trailing slash.
        private static string BuildTag(HttpRequest request)
        {
            var full = request.PathBase.Value + request.Path.Value;
            var index = full.LastIndexOf("/web", StringComparison.OrdinalIgnoreCase);
            var prefix = index > 0 ? full.Substring(0, index) : string.Empty;
            var version = SimklPlugin.Instance?.Version?.ToString() ?? "0";
            return "<script src=\"" + prefix + ScriptPath + "?v=" + Uri.EscapeDataString(version) + "\" defer></script>";
        }

        private async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            if (!HttpMethods.IsGet(context.Request.Method)
                || !IsIndexRequest(context.Request.Path.Value)
                || SimklPlugin.Instance?.Configuration.ShowMenuEntry != true)
            {
                await next().ConfigureAwait(false);
                return;
            }

            // A complete, uncompressed 200 is needed to rewrite the document:
            // no content encoding, and no partial response either.
            context.Request.Headers.Remove("Accept-Encoding");
            context.Request.Headers.Remove("Range");
            context.Request.Headers.Remove("If-Range");

            var original = context.Response.Body;
            using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next().ConfigureAwait(false);
            }
            catch
            {
                // Not ours to swallow: nothing reached the real stream yet, so
                // the host can still answer with a clean error.
                context.Response.Body = original;
                throw;
            }

            context.Response.Body = original;
            buffer.Seek(0, SeekOrigin.Begin);

            var isHtml = context.Response.StatusCode == 200
                && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);
            if (!isHtml)
            {
                // 304, redirects, anything unexpected: passed through as is.
                await buffer.CopyToAsync(original).ConfigureAwait(false);
                return;
            }

            string html;
            using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 1024, leaveOpen: true))
            {
                html = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            try
            {
                var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (bodyClose >= 0 && html.IndexOf(ScriptPath, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    html = html.Substring(0, bodyClose) + BuildTag(context.Request) + "\n" + html.Substring(bodyClose);
                    if (Interlocked.Exchange(ref _announced, 1) == 0)
                    {
                        _logger.LogInformation("Menu entry script added to the web client");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not add the menu script to index.html, serving it unchanged");
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength = bytes.Length;

            // The body changed, so the static handler's validators no longer
            // describe it, and ranges were refused on the way in.
            context.Response.Headers.Remove("ETag");
            context.Response.Headers.Remove("Last-Modified");
            context.Response.Headers.Remove("Accept-Ranges");
            await original.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
        }
    }
}
