namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Threading.Tasks;

    public class TypeTextCommand : ActionEditorCommand
    {
        private const String TextControlName = "text";
        private const String DelayControlName = "delay";
        private const Int32 DefaultDelayMilliseconds = 25;
        private const Int32 MaximumDelayMilliseconds = 1000;
        private const String HoldControlName = "hold";
        private const Int32 DefaultHoldMilliseconds = 40;
        private const Int32 MaximumHoldMilliseconds = 1000;

        private volatile Boolean _isTyping;

        public TypeTextCommand()
        {
            this.DisplayName = "Type Text";
            this.Description = "Types out the given text letter by letter";
            this.GroupName = "Keyboard";

            this.ActionEditor.AddControlEx(new ActionEditorTextbox(TextControlName, "Text", "Text to type").SetRequired());
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(DelayControlName, "Delay (ms)", "Delay between keystrokes").SetFormat(ActionEditorTextboxFormat.Integer).SetPlaceholder(DefaultDelayMilliseconds.ToString()));
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(HoldControlName, "Key hold (ms)", "How long each key is held down").SetFormat(ActionEditorTextboxFormat.Integer).SetPlaceholder(DefaultHoldMilliseconds.ToString()));
        }

        // Called every time the user presses a button assigned to this command.
        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var text = actionParameters.GetString(TextControlName, String.Empty);
            var delayMilliseconds = Math.Clamp(actionParameters.GetInt32(DelayControlName, DefaultDelayMilliseconds), 0, MaximumDelayMilliseconds);
            var holdMilliseconds = Math.Clamp(actionParameters.GetInt32(HoldControlName, DefaultHoldMilliseconds), 0, MaximumHoldMilliseconds);

            if (this._isTyping || String.IsNullOrEmpty(text))
            {
                return false;
            }

            this._isTyping = true;

            // Fire-and-forget: RunCommand must return immediately, so the actual typing happens on a background task.
            _ = this.TypeTextAsync(text, delayMilliseconds, holdMilliseconds);

            return true;
        }

        private async Task TypeTextAsync(String text, Int32 delayMilliseconds, Int32 holdMilliseconds)
        {
            try
            {
                foreach (var c in text)
                {
                    await TypeCharacterAsync(c, holdMilliseconds).ConfigureAwait(false);
                    await Task.Delay(delayMilliseconds).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Failed to type text");
            }
            finally
            {
                this._isTyping = false;
            }
        }

        // Sends the key-down and key-up as separate events with a hold in between. Through nested remote
        // sessions (Horizon) a key-up sent immediately after its key-down can be lost, leaving the key
        // stuck and auto-repeating in the guest.
        private static async Task TypeCharacterAsync(Char c, Int32 holdMilliseconds)
        {
            if (!NativeInput.TryGetKeyForCharacter(c, out var key, out var needsShift))
            {
                NativeInput.TypeCharacter(c);
                return;
            }

            try
            {
                if (needsShift)
                {
                    NativeInput.KeyDown(VirtualKeyCode.Shift);
                }

                NativeInput.KeyDown(key);
                await Task.Delay(holdMilliseconds).ConfigureAwait(false);
            }
            finally
            {
                // Release twice, a little apart, so a dropped key-up doesn't leave the key repeating.
                NativeInput.KeyUp(key);
                await Task.Delay(holdMilliseconds).ConfigureAwait(false);
                NativeInput.KeyUp(key);

                if (needsShift)
                {
                    NativeInput.KeyUp(VirtualKeyCode.Shift);
                }
            }
        }
    }
}
