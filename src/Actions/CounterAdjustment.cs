namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;

    // This class counts both the rotation ticks of a dial and the number of times it is pressed.

    public class CounterAdjustment : PluginDynamicAdjustment
    {
        private Int32 _counter = 0;
        private Int32 _pressCount = 0;
        private Boolean _longPressHandled = false;

        // Initializes the adjustment class.
        // `hasReset` is false: a short press is counted and a long press resets, see ProcessButtonEvent2.
        public CounterAdjustment()
            : base(displayName: "Counter", description: "Counts rotation ticks and presses; long press resets", groupName: "Counters", hasReset: false)
        {
        }

        // This method is called when the adjustment is executed.
        protected override void ApplyAdjustment(String actionParameter, Int32 diff)
        {
            this._counter += diff; // Increase or decrease the counter by the number of ticks.
            this.AdjustmentValueChanged(); // Notify the plugin service that the adjustment value has changed.
        }

        // Handles the dial's press events: a short press (counted on release) adds to the press
        // count, and holding the dial down past the long-press threshold resets both counts.
        protected override Boolean ProcessButtonEvent2(String actionParameter, DeviceButtonEvent2 buttonEvent)
        {
            if (buttonEvent.IsLongPress())
            {
                this._counter = 0;
                this._pressCount = 0;
                this._longPressHandled = true;
                this.AdjustmentValueChanged();

                return true;
            }

            if (!buttonEvent.IsRelease())
            {
                return false;
            }

            // The release that ends a long press must not also count as a short press.
            if (this._longPressHandled)
            {
                this._longPressHandled = false;

                return true;
            }

            this._pressCount++;
            this.AdjustmentValueChanged();

            return true;
        }

        // Returns the adjustment value that is shown next to the dial: ticks, then presses.
        protected override String GetAdjustmentValue(String actionParameter) => $"{this._counter} ({this._pressCount}x)";
    }
}
