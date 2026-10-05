namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    // Runs a short script of steps separated by semicolons, e.g.
    //   key ctrl+a; key ctrl+c; wait 200; type hello world; key enter; click left
    //
    //   key <combo>     press a key combination: modifiers (ctrl, alt, shift, win) joined with '+'
    //                   and a final key such as a, 5, f5, enter, esc, tab, space, up, pagedown
    //   type <text>     type the text
    //   wait <ms>       pause
    //   click [button]  click the mouse (left, right or middle; default left)
    public class MacroCommand : ActionEditorCommand
    {
        private const String StepsControlName = "steps";
        private const String DelayControlName = "delay";
        private const Int32 DefaultDelayMilliseconds = 50;
        private const Int32 MaximumDelayMilliseconds = 10000;

        private readonly CancellationTokenSource _unloading = new CancellationTokenSource();
        private readonly HashSet<String> _running = new HashSet<String>();

        public MacroCommand()
        {
            this.DisplayName = "Key Sequence / Macro";
            this.Description = "Runs a sequence of key presses, typed text, waits and mouse clicks";
            this.GroupName = "Keyboard";

            this.ActionEditor.AddControlEx(new ActionEditorTextbox(StepsControlName, "Steps", "key ctrl+c; wait 200; type hello; key enter; click left").SetRequired());
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(DelayControlName, "Step delay (ms)", "Pause after every step").SetFormat(ActionEditorTextboxFormat.Integer).SetPlaceholder(DefaultDelayMilliseconds.ToString()));
        }

        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            var script = actionParameters.GetString(StepsControlName, String.Empty);
            var delay = Math.Clamp(actionParameters.GetInt32(DelayControlName, DefaultDelayMilliseconds), 0, MaximumDelayMilliseconds);

            if (!TryParse(script, out var steps, out var error))
            {
                PluginLog.Error($"Macro not run: {error}");

                return false;
            }

            lock (this._running)
            {
                // Ignore presses while this macro is still running.
                if (!this._running.Add(script))
                {
                    return false;
                }
            }

            // Fire-and-forget: RunCommand must return immediately.
            _ = this.RunStepsAsync(script, steps, delay);

            return true;
        }

        private async Task RunStepsAsync(String script, List<Action<CancellationToken>> steps, Int32 delay)
        {
            var cancellationToken = this._unloading.Token;

            try
            {
                foreach (var step in steps)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    step(cancellationToken);

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected: the plugin is unloading.
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Macro failed");
            }
            finally
            {
                lock (this._running)
                {
                    this._running.Remove(script);
                }
            }
        }

        protected override Boolean OnUnload()
        {
            this._unloading.Cancel();

            return base.OnUnload();
        }

        // Parses the whole script up front so a typo fails before anything is sent, rather than
        // half way through the macro.
        private static Boolean TryParse(String script, out List<Action<CancellationToken>> steps, out String error)
        {
            steps = new List<Action<CancellationToken>>();
            error = null;

            foreach (var rawStep in script.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var step = rawStep.Trim();

                if (step.Length == 0)
                {
                    continue;
                }

                var separator = step.IndexOf(' ');
                var verb = (separator < 0 ? step : step.Substring(0, separator)).ToLowerInvariant();

                // Keep the argument's own spacing for "type", but trim it for everything else.
                var rawArgument = separator < 0 ? String.Empty : step.Substring(separator + 1);
                var argument = rawArgument.Trim();

                switch (verb)
                {
                    case "type":
                        steps.Add(_ => TypeText(rawArgument));
                        break;

                    case "wait":
                        if (!Int32.TryParse(argument, out var milliseconds) || milliseconds < 0)
                        {
                            error = $"'{step}': wait needs a number of milliseconds";

                            return false;
                        }

                        var waitMilliseconds = Math.Min(milliseconds, 60000);

                        steps.Add(token => token.WaitHandle.WaitOne(waitMilliseconds));
                        break;

                    case "click":
                        var button = argument.ToLowerInvariant() switch
                        {
                            "" or "left" => MouseButton.Left,
                            "right" => MouseButton.Right,
                            "middle" => MouseButton.Middle,
                            _ => MouseButton.None,
                        };

                        if (button == MouseButton.None)
                        {
                            error = $"'{step}': click button must be left, right or middle";

                            return false;
                        }

                        steps.Add(_ => NativeInput.Click(button));
                        break;

                    case "key":
                        if (!TryParseCombination(argument, out var modifiers, out var key))
                        {
                            error = $"'{step}': unrecognised key combination";

                            return false;
                        }

                        steps.Add(_ => PressCombination(modifiers, key));
                        break;

                    default:
                        error = $"'{step}': unknown step '{verb}' (use key, type, wait or click)";

                        return false;
                }
            }

            if (steps.Count == 0)
            {
                error = "no steps";

                return false;
            }

            return true;
        }

        private static void TypeText(String text)
        {
            foreach (var character in text)
            {
                NativeInput.TypeCharacter(character);
            }
        }

        private static void PressCombination(List<VirtualKeyCode> modifiers, VirtualKeyCode key)
        {
            foreach (var modifier in modifiers)
            {
                NativeInput.KeyDown(modifier);
            }

            NativeInput.KeyDown(key);
            NativeInput.KeyUp(key);

            for (var i = modifiers.Count - 1; i >= 0; i--)
            {
                NativeInput.KeyUp(modifiers[i]);
            }
        }

        private static Boolean TryParseCombination(String text, out List<VirtualKeyCode> modifiers, out VirtualKeyCode key)
        {
            modifiers = new List<VirtualKeyCode>();
            key = VirtualKeyCode.None;

            var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
            {
                return false;
            }

            for (var i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers.Add(VirtualKeyCode.Control);
                        break;
                    case "alt":
                        modifiers.Add(VirtualKeyCode.Alt);
                        break;
                    case "shift":
                        modifiers.Add(VirtualKeyCode.Shift);
                        break;
                    case "win":
                    case "windows":
                    case "cmd":
                        modifiers.Add(VirtualKeyCode.WindowsLeft);
                        break;
                    default:
                        return false;
                }
            }

            return TryParseKey(parts[^1], out key);
        }

        private static Boolean TryParseKey(String name, out VirtualKeyCode key)
        {
            var lower = name.ToLowerInvariant();

            if (lower.Length == 1 && lower[0] >= 'a' && lower[0] <= 'z')
            {
                key = (VirtualKeyCode)('A' + (lower[0] - 'a'));

                return true;
            }

            if (lower.Length == 1 && lower[0] >= '0' && lower[0] <= '9')
            {
                key = (VirtualKeyCode)('0' + (lower[0] - '0'));

                return true;
            }

            // F1..F24 are consecutive from 0x70.
            if (lower.Length >= 2 && lower[0] == 'f' && Int32.TryParse(lower.AsSpan(1), out var functionNumber) && functionNumber >= 1 && functionNumber <= 24)
            {
                key = (VirtualKeyCode)(0x6F + functionNumber);

                return true;
            }

            var alias = lower switch
            {
                "enter" => "Return",
                "esc" => "Escape",
                "backspace" => "Back",
                "del" => "Delete",
                "ins" => "Insert",
                "up" => "ArrowUp",
                "down" => "ArrowDown",
                "left" => "ArrowLeft",
                "right" => "ArrowRight",
                "pgup" => "PageUp",
                "pgdn" => "PageDown",
                _ => name,
            };

            return Enum.TryParse(alias, true, out key) && key != VirtualKeyCode.None;
        }
    }
}
