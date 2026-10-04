namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Threading.Tasks;

    public class TypeTextCommand : ActionEditorCommand
    {
        private const String TextControlName = "text";
        private const String DelayControlName = "delay";
        private const Int32 DefaultDelayMilliseconds = 50;
        private const Int32 MaximumDelayMilliseconds = 1000;

        private volatile Boolean _isTyping;

        public TypeTextCommand()
        {
            this.DisplayName = "Type Text";
            this.Description = "Types out the given text letter by letter";
            this.GroupName = "Keyboard";

            this.ActionEditor.AddControlEx(new ActionEditorTextbox(TextControlName, "Text", "Text to type").SetRequired());
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(DelayControlName, "Delay (ms)", "Delay between keystrokes").SetFormat(ActionEditorTextboxFormat.Integer).SetPlaceholder(DefaultDelayMilliseconds.ToString()));
        }

        // Called every time the user presses a button assigned to this command.
        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var text = actionParameters.GetString(TextControlName, String.Empty);
            var delayMilliseconds = Math.Clamp(actionParameters.GetInt32(DelayControlName, DefaultDelayMilliseconds), 0, MaximumDelayMilliseconds);

            if (this._isTyping || String.IsNullOrEmpty(text))
            {
                return false;
            }

            this._isTyping = true;

            // Fire-and-forget: RunCommand must return immediately, so the actual typing happens on a background task.
            _ = this.TypeTextAsync(text, delayMilliseconds);

            return true;
        }

        private async Task TypeTextAsync(String text, Int32 delayMilliseconds)
        {
            try
            {
                foreach (var c in text)
                {
                    this.Plugin.ClientApplication.SendKeyboardShortcut(c, ModifierKey.None);
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
    }
}
