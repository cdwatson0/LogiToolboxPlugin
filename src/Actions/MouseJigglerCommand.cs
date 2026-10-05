namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    // Nudges the mouse pointer one pixel and back on a timer, which keeps the machine awake and
    // chat/remote-desktop status "active". Press to start, press again to stop.
    public class MouseJigglerCommand : ToggleCommandBase
    {
        private const String IntervalControlName = "interval";
        private const Int32 DefaultIntervalSeconds = 30;
        private const Int32 MinimumIntervalSeconds = 1;

        public MouseJigglerCommand()
        {
            this.DisplayName = "Mouse Jiggler";
            this.Description = "Nudges the mouse pointer periodically to keep the computer awake, until pressed again";
            this.GroupName = "Mouse";

            this.ActionEditor.AddControlEx(new ActionEditorTextbox(IntervalControlName, "Interval (s)", "Seconds between nudges").SetFormat(ActionEditorTextboxFormat.Integer).SetPlaceholder(DefaultIntervalSeconds.ToString()));
        }

        protected override Boolean IsConfigured(ActionEditorActionParameters actionParameters) => true;

        protected override String GetStateKey(ActionEditorActionParameters actionParameters) =>
            GetIntervalSeconds(actionParameters).ToString();

        protected override String GetLabel(ActionEditorActionParameters actionParameters) =>
            $"Jiggler\n{GetIntervalSeconds(actionParameters)} s";

        protected override async Task RunAsync(ActionEditorActionParameters actionParameters, CancellationToken cancellationToken)
        {
            var interval = TimeSpan.FromSeconds(GetIntervalSeconds(actionParameters));

            while (!cancellationToken.IsCancellationRequested)
            {
                // Relative moves, so the pointer ends up exactly where it started.
                NativeInput.MoveMouseBy(1, 0);
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                NativeInput.MoveMouseBy(-1, 0);

                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }

        private static Int32 GetIntervalSeconds(ActionEditorActionParameters actionParameters) =>
            Math.Max(MinimumIntervalSeconds, actionParameters.GetInt32(IntervalControlName, DefaultIntervalSeconds));
    }
}
