namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    // Holds a key combination down until the button is pressed again. Button commands only get a
    // "pressed" event (there is no release event), so this is a toggle rather than a true
    // hold-while-pressed.
    public class HoldKeyCommand : ToggleCommandBase
    {
        private const String KeyControlName = "key";
        private const String DefaultLabel = "Hold\nKey";

        public HoldKeyCommand()
        {
            this.DisplayName = "Hold Key";
            this.Description = "Holds a key combination down until pressed again";
            this.GroupName = "Keyboard";

            this.ActionEditor.AddControlEx(new ActionEditorKeyboardKey(KeyControlName, "Key combination", "Key combination to hold down").SetBehavior(ActionEditorKeyboardKeyBehavior.KeyboardKey).SetRequired());
        }

        protected override Boolean IsConfigured(ActionEditorActionParameters actionParameters) =>
            TryGetKeyboardKey(actionParameters, out _);

        protected override String GetStateKey(ActionEditorActionParameters actionParameters) =>
            actionParameters.GetString(KeyControlName, String.Empty);

        protected override String GetLabel(ActionEditorActionParameters actionParameters)
        {
            if (!TryGetKeyboardKeyParameter(actionParameters, out var keyboardKeyParameter))
            {
                return DefaultLabel;
            }

            var label = keyboardKeyParameter.DisplayName;

            if (String.IsNullOrEmpty(label))
            {
                label = keyboardKeyParameter.KeyboardKey?.GetDisplayName();
            }

            return String.IsNullOrEmpty(label) ? DefaultLabel : "Hold\n" + label;
        }

        protected override async Task RunAsync(ActionEditorActionParameters actionParameters, CancellationToken cancellationToken)
        {
            if (!TryGetKeyboardKey(actionParameters, out var keyboardKey))
            {
                return;
            }

            var modifiers = GetModifierKeyCodes(keyboardKey.ModifierKey);

            try
            {
                foreach (var modifier in modifiers)
                {
                    NativeInput.KeyDown(modifier);
                }

                NativeInput.KeyDown(keyboardKey.VirtualKeyCode);

                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                NativeInput.KeyUp(keyboardKey.VirtualKeyCode);

                for (var i = modifiers.Length - 1; i >= 0; i--)
                {
                    NativeInput.KeyUp(modifiers[i]);
                }
            }
        }

        private static VirtualKeyCode[] GetModifierKeyCodes(ModifierKey modifierKey)
        {
            var codes = new System.Collections.Generic.List<VirtualKeyCode>();

            if ((modifierKey & (ModifierKey.Control | ModifierKey.ControlOrCommand)) != 0)
            {
                codes.Add(VirtualKeyCode.Control);
            }

            if ((modifierKey & ModifierKey.Alt) != 0)
            {
                codes.Add(VirtualKeyCode.Alt);
            }

            if ((modifierKey & ModifierKey.Shift) != 0)
            {
                codes.Add(VirtualKeyCode.Shift);
            }

            if ((modifierKey & ModifierKey.Win) != 0)
            {
                codes.Add(VirtualKeyCode.WindowsLeft);
            }

            return codes.ToArray();
        }

        private static Boolean TryGetKeyboardKeyParameter(ActionEditorActionParameters actionParameters, out ActionEditorKeyboardKeyParameter keyboardKeyParameter)
        {
            keyboardKeyParameter = null;

            return actionParameters.TryGetString(KeyControlName, out var parameterValue) && ActionEditorKeyboardKeyParameter.TryParse(parameterValue, out keyboardKeyParameter) && keyboardKeyParameter.IsKeyboardKey();
        }

        private static Boolean TryGetKeyboardKey(ActionEditorActionParameters actionParameters, out KeyboardKey keyboardKey)
        {
            keyboardKey = null;

            if (!TryGetKeyboardKeyParameter(actionParameters, out var keyboardKeyParameter))
            {
                return false;
            }

            keyboardKey = keyboardKeyParameter.KeyboardKey;

            return (keyboardKey != null) && !keyboardKey.IsEmpty();
        }
    }
}
