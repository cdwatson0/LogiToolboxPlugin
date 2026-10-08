namespace Loupedeck.LogiToolboxPlugin
{
    using System;
    using System.Runtime.InteropServices;

    // Thin wrapper over the Win32 SendInput API. ClientApplication can only send a complete key
    // press (down + up) and has no mouse support, so anything that needs a held key, a mouse move
    // or a mouse click goes through here instead. Windows only.
    internal static class NativeInput
    {
        private const UInt32 InputMouse = 0;
        private const UInt32 InputKeyboard = 1;

        private const UInt32 KeyEventExtendedKey = 0x0001;
        private const UInt32 KeyEventKeyUp = 0x0002;
        private const UInt32 KeyEventUnicode = 0x0004;

        private const UInt32 MouseEventMove = 0x0001;
        private const UInt32 MouseEventLeftDown = 0x0002;
        private const UInt32 MouseEventLeftUp = 0x0004;
        private const UInt32 MouseEventRightDown = 0x0008;
        private const UInt32 MouseEventRightUp = 0x0010;
        private const UInt32 MouseEventMiddleDown = 0x0020;
        private const UInt32 MouseEventMiddleUp = 0x0040;

        public static void KeyDown(VirtualKeyCode virtualKeyCode) => SendKey(virtualKeyCode, false);

        public static void KeyUp(VirtualKeyCode virtualKeyCode) => SendKey(virtualKeyCode, true);

        // Types one character regardless of keyboard layout, by sending it as a Unicode code unit.
        public static void TypeCharacter(Char character)
        {
            var down = new Input { Type = InputKeyboard };
            down.Data.Keyboard = new KeyboardInput { ScanCode = character, Flags = KeyEventUnicode };

            var up = new Input { Type = InputKeyboard };
            up.Data.Keyboard = new KeyboardInput { ScanCode = character, Flags = KeyEventUnicode | KeyEventKeyUp };

            Send(down, up);
        }

        // Maps a character to the key (and Shift state) that produces it on the current keyboard layout.
        public static Boolean TryGetKeyForCharacter(Char character, out VirtualKeyCode virtualKeyCode, out Boolean needsShift)
        {
            var result = VkKeyScan(character);
            var key = (Byte)(result & 0xFF);
            var shiftState = (result >> 8) & 0xFF;

            virtualKeyCode = (VirtualKeyCode)key;
            needsShift = (shiftState & 1) != 0;

            // -1 means no key produces the character; Ctrl/Alt combinations (AltGr) aren't handled here.
            return result != -1 && (shiftState & ~1) == 0;
        }

        public static void MoveMouseBy(Int32 deltaX, Int32 deltaY)
        {
            var input = new Input { Type = InputMouse };
            input.Data.Mouse = new MouseInput { Dx = deltaX, Dy = deltaY, Flags = MouseEventMove };

            Send(input);
        }

        public static void MouseDown(MouseButton button) => SendMouseButton(button, true);

        public static void MouseUp(MouseButton button) => SendMouseButton(button, false);

        public static void Click(MouseButton button)
        {
            SendMouseButton(button, true);
            SendMouseButton(button, false);
        }

        private static void SendKey(VirtualKeyCode virtualKeyCode, Boolean isKeyUp)
        {
            var code = (UInt16)virtualKeyCode;
            var flags = isKeyUp ? KeyEventKeyUp : 0;

            if (IsExtendedKey(code))
            {
                flags |= KeyEventExtendedKey;
            }

            var input = new Input { Type = InputKeyboard };
            input.Data.Keyboard = new KeyboardInput { VirtualKey = code, ScanCode = (UInt16)MapVirtualKey(code, 0), Flags = flags };

            Send(input);
        }

        private static void SendMouseButton(MouseButton button, Boolean isDown)
        {
            var flags = button switch
            {
                MouseButton.Right => isDown ? MouseEventRightDown : MouseEventRightUp,
                MouseButton.Middle => isDown ? MouseEventMiddleDown : MouseEventMiddleUp,
                _ => isDown ? MouseEventLeftDown : MouseEventLeftUp,
            };

            var input = new Input { Type = InputMouse };
            input.Data.Mouse = new MouseInput { Flags = flags };

            Send(input);
        }

        // Keys that Windows reports with the "extended" flag: without it, e.g. the arrow keys
        // arrive as numpad keys.
        private static Boolean IsExtendedKey(UInt16 virtualKey) =>
            (virtualKey >= 0x21 && virtualKey <= 0x2E)   // PageUp .. Delete (incl. arrows, End, Home, Insert)
            || virtualKey == 0x5B || virtualKey == 0x5C  // Left / right Windows
            || virtualKey == 0x6F                        // Numpad divide
            || virtualKey == 0x90                        // NumLock
            || virtualKey == 0xA3 || virtualKey == 0xA5; // Right Ctrl / Alt

        private static void Send(params Input[] inputs) =>
            SendInput((UInt32)inputs.Length, inputs, Marshal.SizeOf<Input>());

        [DllImport("user32.dll", SetLastError = true)]
        private static extern UInt32 SendInput(UInt32 inputCount, Input[] inputs, Int32 size);

        [DllImport("user32.dll")]
        private static extern Int16 VkKeyScan(Char character);

        [DllImport("user32.dll")]
        private static extern UInt32 MapVirtualKey(UInt32 code, UInt32 mapType);

        [StructLayout(LayoutKind.Sequential)]
        private struct Input
        {
            public UInt32 Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MouseInput Mouse;

            [FieldOffset(0)]
            public KeyboardInput Keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public Int32 Dx;
            public Int32 Dy;
            public UInt32 MouseData;
            public UInt32 Flags;
            public UInt32 Time;
            public IntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public UInt16 VirtualKey;
            public UInt16 ScanCode;
            public UInt32 Flags;
            public UInt32 Time;
            public IntPtr ExtraInfo;
        }
    }
}
