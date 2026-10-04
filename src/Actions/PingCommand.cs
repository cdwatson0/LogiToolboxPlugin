namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Collections.Concurrent;
    using System.Net.NetworkInformation;
    using System.Threading.Tasks;

    // This command uses the Action Editor so that each button it's assigned to can ping
    // its own host, configured per-button in the Loupedeck software.
    public class PingCommand : ActionEditorCommand
    {
        private const String HostControlName = "host";

        // How long to wait for a reply before giving up.
        private const Int32 TimeoutMilliseconds = 2000;

        // Keyed by host: this command instance is shared by every button it's assigned to,
        // and each button can be configured with a different host.
        private readonly ConcurrentDictionary<String, Boolean> _isPinging = new ConcurrentDictionary<String, Boolean>();
        private readonly ConcurrentDictionary<String, String> _lastResultText = new ConcurrentDictionary<String, String>();

        public PingCommand()
        {
            this.DisplayName = "Ping";
            this.Description = "Pings a host and shows the response time";
            this.GroupName = "Network";

            this.ActionEditor.AddControlEx(new ActionEditorTextbox(HostControlName, "Host", "Host name or IP address to ping").SetRequired());
        }

        // Called every time the user presses a button assigned to this command.
        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var host = actionParameters.GetString(HostControlName, String.Empty);

            if (String.IsNullOrEmpty(host) || !this._isPinging.TryAdd(host, true))
            {
                // Either not configured yet, or a ping to this host is already in flight.
                return false;
            }

            this.ActionImageChanged();

            // Fire-and-forget: RunCommand must return immediately, so the actual ping
            // (and the button refresh) happens on a background task.
            _ = this.PingAndUpdateButtonAsync(host);

            return true;
        }

        private async Task PingAndUpdateButtonAsync(String host)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = await ping.SendPingAsync(host, TimeoutMilliseconds).ConfigureAwait(false);

                    this._lastResultText[host] = reply.Status == IPStatus.Success
                        ? $"{reply.RoundtripTime} ms"
                        : reply.Status.ToString();
                }
            }
            catch (Exception ex)
            {
                this._lastResultText[host] = "Error";
                PluginLog.Error(ex, $"Ping to {host} failed");
            }
            finally
            {
                this._isPinging.TryRemove(host, out _);

                // Tell Logi Plugin Service to redraw this button with the new result.
                this.ActionImageChanged();
            }
        }

        // Draws the button image: the host name and the last ping result.
        protected override BitmapImage GetCommandImage(ActionEditorActionParameters actionParameters, Int32 imageWidth, Int32 imageHeight)
        {
            var host = actionParameters.GetString(HostControlName, String.Empty);

            using (var bitmapBuilder = new BitmapBuilder(imageWidth, imageHeight))
            {
                if (String.IsNullOrEmpty(host))
                {
                    bitmapBuilder.DrawText("Ping");
                }
                else
                {
                    var resultText = this._isPinging.ContainsKey(host)
                        ? "..."
                        : this._lastResultText.GetValueOrDefault(host, "Ping");

                    bitmapBuilder.DrawText($"{host}\n{resultText}");
                }

                return bitmapBuilder.ToImage();
            }
        }
    }
}
