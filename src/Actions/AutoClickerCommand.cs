namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    // Clicks the mouse at the current pointer position on a timer. Press to start, press again to
    // stop.
    public class AutoClickerCommand : ToggleCommandBase
    {
        private const String ButtonControlName = "button";
        private const String IntervalControlName = "interval";
        private const Int32 DefaultIntervalMilliseconds = 100;

        // Guard against a typo like "1" flooding the system with clicks.
        private const Int32 MinimumIntervalMilliseconds = 10;

        public AutoClickerCommand()
        {
            this.DisplayName = "Auto-Clicker";
            this.Description = "Repeatedly clicks the mouse at the pointer position until pressed again";
            this.GroupName = "Mouse";

            this.ActionEditor.AddControlEx(new ActionEditorListbox(ButtonControlName, "Mouse button", "Mouse button to click"));
            this.ActionEditor.ListboxItemsRequested += this.OnListboxItemsRequested;
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(IntervalControlName, "Interval (ms)", "Delay between clicks").SetFormat(ActionEditorTextboxFormat.Integer).SetPlaceholder(DefaultIntervalMilliseconds.ToString()));
        }

        private void OnListboxItemsRequested(Object sender, ActionEditorListboxItemsRequestedEventArgs e)
        {
            if (e.ControlName != ButtonControlName)
            {
                return;
            }

            e.AddItem("left", "Left", "Left mouse button");
            e.AddItem("right", "Right", "Right mouse button");
            e.AddItem("middle", "Middle", "Middle mouse button");

            // Don't override a button that's already configured.
            if (String.IsNullOrEmpty(e.SelectedItemName))
            {
                e.SetSelectedItemName("left");
            }
        }

        protected override Boolean IsConfigured(ActionEditorActionParameters actionParameters) => true;

        protected override String GetStateKey(ActionEditorActionParameters actionParameters) =>
            $"{GetButton(actionParameters)}|{GetIntervalMilliseconds(actionParameters)}";

        protected override String GetLabel(ActionEditorActionParameters actionParameters) =>
            $"Click\n{GetButton(actionParameters)} {GetIntervalMilliseconds(actionParameters)} ms";

        protected override async Task RunAsync(ActionEditorActionParameters actionParameters, CancellationToken cancellationToken)
        {
            var button = GetButton(actionParameters);
            var interval = GetIntervalMilliseconds(actionParameters);

            while (!cancellationToken.IsCancellationRequested)
            {
                NativeInput.Click(button);

                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }

        private static MouseButton GetButton(ActionEditorActionParameters actionParameters) =>
            actionParameters.GetString(ButtonControlName, String.Empty).Trim().ToLowerInvariant() switch
            {
                "right" => MouseButton.Right,
                "middle" => MouseButton.Middle,
                _ => MouseButton.Left,
            };

        private static Int32 GetIntervalMilliseconds(ActionEditorActionParameters actionParameters) =>
            Math.Max(MinimumIntervalMilliseconds, actionParameters.GetInt32(IntervalControlName, DefaultIntervalMilliseconds));
    }
}
