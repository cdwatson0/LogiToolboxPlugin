namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Threading.Tasks;

    // Requests a URL and shows the HTTP status code and response time on the button, like Ping
    // does for hosts. Each button has its own URL.
    public class HttpCheckCommand : ActionEditorCommand
    {
        private const String UrlControlName = "url";

        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // Keyed by URL: this command instance is shared by every button it's assigned to.
        private readonly ConcurrentDictionary<String, Boolean> _isChecking = new ConcurrentDictionary<String, Boolean>();
        private readonly ConcurrentDictionary<String, String> _lastResultText = new ConcurrentDictionary<String, String>();

        public HttpCheckCommand()
        {
            this.DisplayName = "HTTP Check";
            this.Description = "Requests a URL and shows the status code and response time";
            this.GroupName = "Network";

            this.ActionEditor.AddControlEx(new ActionEditorTextbox(UrlControlName, "URL", "Address to request, e.g. https://example.com").SetRequired());
        }

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var url = NormalizeUrl(actionParameters.GetString(UrlControlName, String.Empty));

            if (url.Length == 0 || !this._isChecking.TryAdd(url, true))
            {
                // Either not configured yet, or a check of this URL is already in flight.
                return false;
            }

            this.ActionImageChanged();

            // Fire-and-forget: RunCommand must return immediately.
            _ = this.CheckAndUpdateButtonAsync(url);

            return true;
        }

        private async Task CheckAndUpdateButtonAsync(String url)
        {
            try
            {
                var stopwatch = Stopwatch.StartNew();

                // Headers only: we want the status and latency, not the body.
                using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    stopwatch.Stop();

                    this._lastResultText[url] = $"{(Int32)response.StatusCode} · {stopwatch.ElapsedMilliseconds} ms";
                }
            }
            catch (TaskCanceledException)
            {
                this._lastResultText[url] = "Timeout";
            }
            catch (HttpRequestException ex)
            {
                this._lastResultText[url] = "Failed";
                PluginLog.Error(ex, $"HTTP check of {url} failed");
            }
            catch (Exception ex)
            {
                this._lastResultText[url] = "Error";
                PluginLog.Error(ex, $"HTTP check of {url} failed");
            }
            finally
            {
                this._isChecking.TryRemove(url, out _);

                this.ActionImageChanged();
            }
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters actionParameters, Int32 imageWidth, Int32 imageHeight)
        {
            var url = NormalizeUrl(actionParameters.GetString(UrlControlName, String.Empty));

            using (var bitmapBuilder = new BitmapBuilder(imageWidth, imageHeight))
            {
                if (url.Length == 0)
                {
                    bitmapBuilder.DrawText("HTTP\nCheck");
                }
                else
                {
                    var resultText = this._isChecking.ContainsKey(url)
                        ? "..."
                        : this._lastResultText.GetValueOrDefault(url, "HTTP");

                    var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url;

                    bitmapBuilder.DrawText($"{host}\n{resultText}");
                }

                return bitmapBuilder.ToImage();
            }
        }

        // Lets the user type "example.com" without the scheme.
        private static String NormalizeUrl(String url)
        {
            url = url.Trim();

            if (url.Length == 0)
            {
                return url;
            }

            return url.Contains("://", StringComparison.Ordinal) ? url : "https://" + url;
        }
    }
}
