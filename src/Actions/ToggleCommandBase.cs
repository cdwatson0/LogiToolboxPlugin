namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    // Shared plumbing for buttons that start something on one press and stop it on the next
    // (Hold Key, Mouse Jiggler, Auto-Clicker). Running state is keyed by the button's
    // configuration because one command instance is shared by every button it's assigned to.
    public abstract class ToggleCommandBase : ActionEditorCommand
    {
        private static readonly BitmapColor BackgroundColor = new BitmapColor(0, 0, 0);
        private static readonly BitmapColor IdleColor = new BitmapColor(140, 140, 140);
        private static readonly BitmapColor RunningColor = new BitmapColor(0, 190, 255);

        private readonly ConcurrentDictionary<String, CancellationTokenSource> _running = new ConcurrentDictionary<String, CancellationTokenSource>();

        // Whether the button has everything it needs to run.
        protected abstract Boolean IsConfigured(ActionEditorActionParameters actionParameters);

        // Identifies this button's configuration in the running-state dictionary.
        protected abstract String GetStateKey(ActionEditorActionParameters actionParameters);

        // Text drawn on the button.
        protected abstract String GetLabel(ActionEditorActionParameters actionParameters);

        // Runs until the token is cancelled. Must clean up after itself (release keys, etc.) in a
        // finally block: it is cancelled both by the stop press and when the plugin unloads.
        protected abstract Task RunAsync(ActionEditorActionParameters actionParameters, CancellationToken cancellationToken);

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!this.IsConfigured(actionParameters))
            {
                return false;
            }

            var stateKey = this.GetStateKey(actionParameters);

            if (this._running.TryRemove(stateKey, out var runningCancellation))
            {
                // Already running: this press is the "stop" press.
                runningCancellation.Cancel();

                return true;
            }

            var cancellationTokenSource = new CancellationTokenSource();

            if (!this._running.TryAdd(stateKey, cancellationTokenSource))
            {
                cancellationTokenSource.Dispose();

                return false;
            }

            this.ActionImageChanged();

            // Fire-and-forget: RunCommand must return immediately.
            _ = this.RunAndCleanUpAsync(actionParameters, stateKey, cancellationTokenSource);

            return true;
        }

        private async Task RunAndCleanUpAsync(ActionEditorActionParameters actionParameters, String stateKey, CancellationTokenSource cancellationTokenSource)
        {
            try
            {
                await this.RunAsync(actionParameters, cancellationTokenSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: the user pressed the button again, or the plugin is unloading.
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"{this.DisplayName} failed");
            }
            finally
            {
                // Only clear the entry if it's still ours: a later press may already have replaced it.
                if (this._running.TryGetValue(stateKey, out var current) && ReferenceEquals(current, cancellationTokenSource))
                {
                    this._running.TryRemove(stateKey, out _);
                }

                cancellationTokenSource.Dispose();

                this.ActionImageChanged();
            }
        }

        protected override BitmapImage GetCommandImage(ActionEditorActionParameters actionParameters, Int32 imageWidth, Int32 imageHeight)
        {
            var isRunning = this._running.ContainsKey(this.GetStateKey(actionParameters));
            var color = isRunning ? RunningColor : IdleColor;

            using (var bitmapBuilder = new BitmapBuilder(imageWidth, imageHeight))
            {
                bitmapBuilder.Clear(BackgroundColor);

                var minimumSide = Math.Min(imageWidth, imageHeight);

                // Status lamp: a ring while idle, a solid disc while running.
                var centerX = imageWidth / 2f;
                var centerY = imageHeight * 0.3f;
                var radius = minimumSide * 0.13f;

                bitmapBuilder.FillCircle(centerX, centerY, radius, color);

                if (!isRunning)
                {
                    bitmapBuilder.FillCircle(centerX, centerY, radius * 0.7f, BackgroundColor);
                }

                var textTop = (Int32)(imageHeight * 0.5);

                bitmapBuilder.DrawText(this.GetLabel(actionParameters), 0, textTop, imageWidth, imageHeight - textTop, color, Math.Max(8, imageHeight / 7));

                return bitmapBuilder.ToImage();
            }
        }

        // Stops everything when the plugin is unloaded (e.g. on a hot reload), which also releases
        // any keys or buttons still held.
        protected override Boolean OnUnload()
        {
            foreach (var stateKey in this._running.Keys)
            {
                if (this._running.TryRemove(stateKey, out var cancellationTokenSource))
                {
                    cancellationTokenSource.Cancel();
                }
            }

            return base.OnUnload();
        }
    }
}
