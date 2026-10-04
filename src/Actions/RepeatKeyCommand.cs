namespace Loupedeck.LogiToolboxPlugin.Actions
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    // This command uses the Action Editor so that each button it's assigned to can have its own
    // key combination and repeat interval, configured per-button in the Loupedeck software.
    // Pressing the button starts sending the key combination to the foreground application on a
    // timer; pressing it again stops.
    public class RepeatKeyCommand : ActionEditorCommand
    {
        private const String KeyControlName = "key";
        private const String IntervalControlName = "interval";
        private const Int32 DefaultIntervalMilliseconds = 1000;

        // Guard against a typo like "1" turning into a key press every millisecond.
        private const Int32 MinimumIntervalMilliseconds = 20;

        // Every key press flashes the button. The flash lasts a fraction of the interval so that
        // consecutive presses stay visually separate, and it is skipped entirely below
        // MinimumFlashMilliseconds, where it would be too short to see and would only cost redraws.
        private const Int32 MinimumFlashMilliseconds = 40;
        private const Int32 MaximumFlashMilliseconds = 90;

        private const String DefaultLabel = "Repeat\nKey";

        private static readonly BitmapColor BackgroundColor = new BitmapColor(0, 0, 0);
        private static readonly BitmapColor IdleColor = new BitmapColor(140, 140, 140);
        private static readonly BitmapColor RunningColor = new BitmapColor(0, 190, 255);
        private static readonly BitmapColor FlashColor = BitmapColor.White;
        private static readonly BitmapColor FlashHaloColor = new BitmapColor(0, 190, 255, 90);

        // Keyed by configuration: this command instance is shared by every button it's assigned to,
        // and each button can be configured with a different key combination and interval.
        private readonly ConcurrentDictionary<String, CancellationTokenSource> _running = new ConcurrentDictionary<String, CancellationTokenSource>();
        private readonly ConcurrentDictionary<String, Boolean> _flashing = new ConcurrentDictionary<String, Boolean>();

        public RepeatKeyCommand()
        {
            this.DisplayName = "Repeat Key Combination";
            this.Description = "Repeatedly sends a key combination to the foreground application until pressed again";
            this.GroupName = "Keyboard";

            this.ActionEditor.AddControlEx(new ActionEditorKeyboardKey(KeyControlName, "Key combination", "Key combination to send").SetBehavior(ActionEditorKeyboardKeyBehavior.KeyboardKey).SetRequired());
            this.ActionEditor.AddControlEx(new ActionEditorTextbox(IntervalControlName, "Interval (ms)", "Delay between repeats").SetFormat(ActionEditorTextboxFormat.Integer).SetPlaceholder(DefaultIntervalMilliseconds.ToString()));
        }

        // Called every time the user presses a button assigned to this command: starts the repeat
        // loop, or stops it if this button's configuration is already repeating.
        protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
        {
            if (!TryGetKeyboardKey(actionParameters, out var keyboardKey))
            {
                // Not configured yet.
                return false;
            }

            var stateKey = GetStateKey(actionParameters);

            if (this._running.TryRemove(stateKey, out var runningCancellation))
            {
                // Already repeating: this press is the "stop" press.
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

            // Fire-and-forget: RunCommand must return immediately, so the repeating happens on a
            // background task.
            _ = this.RepeatKeyAsync(keyboardKey, GetIntervalMilliseconds(actionParameters), stateKey, cancellationTokenSource);

            return true;
        }

        private async Task RepeatKeyAsync(KeyboardKey keyboardKey, Int32 intervalMilliseconds, String stateKey, CancellationTokenSource cancellationTokenSource)
        {
            var flashMilliseconds = Math.Min(MaximumFlashMilliseconds, intervalMilliseconds / 3);

            if (flashMilliseconds < MinimumFlashMilliseconds)
            {
                // Repeating too fast for a flash to be seen: the button just stays in its running state.
                flashMilliseconds = 0;
            }

            try
            {
                var cancellationToken = cancellationTokenSource.Token;

                while (!cancellationToken.IsCancellationRequested)
                {
                    this.Plugin.ClientApplication.SendKeyboardShortcut(keyboardKey);

                    if (flashMilliseconds == 0)
                    {
                        await Task.Delay(intervalMilliseconds, cancellationToken).ConfigureAwait(false);

                        continue;
                    }

                    // Flash on for this press, then back to the plain running state for the rest of
                    // the interval.
                    this._flashing[stateKey] = true;
                    this.ActionImageChanged();

                    await Task.Delay(flashMilliseconds, cancellationToken).ConfigureAwait(false);

                    this._flashing.TryRemove(stateKey, out _);
                    this.ActionImageChanged();

                    await Task.Delay(intervalMilliseconds - flashMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected: the user pressed the button again, or the plugin is unloading.
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, $"Failed to send key combination {keyboardKey}");
            }
            finally
            {
                this._flashing.TryRemove(stateKey, out _);

                // Only clear the entry if it's still ours: a later press may already have replaced
                // it with a new loop.
                if (this._running.TryGetValue(stateKey, out var currentCancellation) && ReferenceEquals(currentCancellation, cancellationTokenSource))
                {
                    this._running.TryRemove(stateKey, out _);
                }

                cancellationTokenSource.Dispose();

                // Tell Logi Plugin Service to redraw this button in its stopped state.
                this.ActionImageChanged();
            }
        }

        // Draws the button image: a circular arrow plus the configured key combination, dim while
        // stopped, coloured while repeating, and flashed white on each key press.
        protected override BitmapImage GetCommandImage(ActionEditorActionParameters actionParameters, Int32 imageWidth, Int32 imageHeight)
        {
            var stateKey = GetStateKey(actionParameters);
            var isRunning = this._running.ContainsKey(stateKey);
            var isFlashing = isRunning && this._flashing.ContainsKey(stateKey);

            using (var bitmapBuilder = new BitmapBuilder(imageWidth, imageHeight))
            {
                bitmapBuilder.Clear(BackgroundColor);

                DrawRepeatGlyph(bitmapBuilder, imageWidth, imageHeight, isRunning, isFlashing);

                var textColor = isFlashing ? FlashColor : (isRunning ? RunningColor : IdleColor);
                var textTop = (Int32)(imageHeight * 0.62);

                bitmapBuilder.DrawText(GetLabel(actionParameters), 0, textTop, imageWidth, imageHeight - textTop, textColor, Math.Max(8, imageHeight / 7));

                return bitmapBuilder.ToImage();
            }
        }

        // Draws a circular arrow. The ring is stroked as a run of short chords and the arrow head
        // is filled with a fan of lines, because BitmapBuilder has no primitive that would do
        // either: DrawArc fills its path instead of stroking it, DrawCircle only strokes
        // hairline-thin, and there is no filled polygon at all.
        private static void DrawRepeatGlyph(BitmapBuilder bitmapBuilder, Int32 imageWidth, Int32 imageHeight, Boolean isRunning, Boolean isFlashing)
        {
            // Angles are in degrees, 0 at 3 o'clock, increasing clockwise (y grows downwards).
            // The ring stops short of a full turn, leaving a gap at the top for the arrow head.
            const Single StartAngle = 305f;
            const Single SweepAngle = 290f;
            const Int32 SegmentCount = 48;

            var color = isFlashing ? FlashColor : (isRunning ? RunningColor : IdleColor);
            var minimumSide = Math.Min(imageWidth, imageHeight);
            var centerX = imageWidth / 2f;
            var centerY = imageHeight * 0.36f;

            // The glyph swells slightly on a flash, so a press reads as movement and not just as a
            // change of colour.
            var radius = minimumSide * (isFlashing ? 0.215f : 0.2f);
            var strokeWidth = Math.Max(2f, minimumSide * (isFlashing ? 0.065f : 0.05f));

            if (isFlashing)
            {
                bitmapBuilder.FillCircle(centerX, centerY, radius * 1.7f, FlashHaloColor);
            }

            var previousX = 0f;
            var previousY = 0f;

            for (var segment = 0; segment <= SegmentCount; segment++)
            {
                var angle = ToRadians(StartAngle + (SweepAngle * segment / SegmentCount));
                var x = centerX + (radius * (Single)Math.Cos(angle));
                var y = centerY + (radius * (Single)Math.Sin(angle));

                if (segment > 0)
                {
                    bitmapBuilder.DrawLine(previousX, previousY, x, y, color, strokeWidth);
                }

                // The chords are drawn with flat ends, so round off each join by hand — without
                // this the ring is visibly notched.
                bitmapBuilder.FillCircle(x, y, strokeWidth / 2f, color);

                previousX = x;
                previousY = y;
            }

            // Arrow head at the end of the sweep: its base sits across the ring and its tip points
            // the way the ring travels.
            var endAngle = ToRadians(StartAngle + SweepAngle);
            var radialX = (Single)Math.Cos(endAngle);
            var radialY = (Single)Math.Sin(endAngle);
            var headLength = radius * 0.62f;
            var headWidth = radius * 0.40f;
            var tipX = previousX - (radialY * headLength);
            var tipY = previousY + (radialX * headLength);

            FillTriangle(
                bitmapBuilder,
                previousX + (radialX * headWidth),
                previousY + (radialY * headWidth),
                previousX - (radialX * headWidth),
                previousY - (radialY * headWidth),
                tipX,
                tipY,
                color);
        }

        // Fills the triangle (x1,y1)-(x2,y2)-(tipX,tipY) by sweeping a line from its base to its
        // tip, since BitmapBuilder can only draw lines, rectangles and circles.
        private static void FillTriangle(BitmapBuilder bitmapBuilder, Single x1, Single y1, Single x2, Single y2, Single tipX, Single tipY, BitmapColor color)
        {
            const Int32 StepCount = 32;

            // Overlap consecutive lines so the fill has no seams between them.
            var stepWidth = Math.Max(1.5f, Distance(x1, y1, tipX, tipY) / StepCount * 2.5f);

            for (var step = 0; step <= StepCount; step++)
            {
                var progress = (Single)step / StepCount;

                bitmapBuilder.DrawLine(
                    x1 + ((tipX - x1) * progress),
                    y1 + ((tipY - y1) * progress),
                    x2 + ((tipX - x2) * progress),
                    y2 + ((tipY - y2) * progress),
                    color,
                    stepWidth);
            }
        }

        private static Single Distance(Single x1, Single y1, Single x2, Single y2) =>
            (Single)Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));

        // Stops every running loop when the plugin is unloaded (e.g. on a hot reload).
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

        private static Single ToRadians(Single degrees) =>
            (Single)(degrees * Math.PI / 180.0);

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

        // The name of the configured key combination, or a placeholder while the button has none —
        // which is also what the action shows in the software's action list.
        private static String GetLabel(ActionEditorActionParameters actionParameters)
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

            return String.IsNullOrEmpty(label) ? DefaultLabel : label;
        }

        private static Int32 GetIntervalMilliseconds(ActionEditorActionParameters actionParameters) =>
            Math.Max(MinimumIntervalMilliseconds, actionParameters.GetInt32(IntervalControlName, DefaultIntervalMilliseconds));

        // The action instance is shared across buttons, so running state is keyed by the values
        // that make one button's configuration different from another's.
        private static String GetStateKey(ActionEditorActionParameters actionParameters) =>
            $"{actionParameters.GetString(KeyControlName, String.Empty)}|{actionParameters.GetString(IntervalControlName, String.Empty)}";
    }
}
