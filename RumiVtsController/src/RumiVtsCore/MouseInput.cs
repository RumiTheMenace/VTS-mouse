using System.Runtime.InteropServices;
using System.Text;

namespace RumiVtsController
{
    internal sealed class MouseInput
    {
        public Point GetCursorPosition()
        {
            return Cursor.Position;
        }

        public bool IsCursorVisible()
        {
            var info = new CursorInfo
            {
                Size = Marshal.SizeOf<CursorInfo>()
            };

            if (!GetCursorInfo(ref info))
            {
                return true;
            }

            return (info.Flags & CursorShowing) != 0;
        }

        public bool IsForegroundFullscreen(Screen screen)
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                return false;
            }

            if (!GetWindowRect(handle, out var rect))
            {
                return false;
            }

            var bounds = screen.Bounds;
            const int tolerance = 2;
            return Math.Abs(rect.Left - bounds.Left) <= tolerance
                && Math.Abs(rect.Top - bounds.Top) <= tolerance
                && Math.Abs(rect.Right - bounds.Right) <= tolerance
                && Math.Abs(rect.Bottom - bounds.Bottom) <= tolerance;
        }

        public bool TryGetForegroundWindowBounds(out Rectangle bounds)
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                bounds = Rectangle.Empty;
                return false;
            }

            if (!GetWindowRect(handle, out var rect))
            {
                bounds = Rectangle.Empty;
                return false;
            }

            bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return !bounds.IsEmpty;
        }

        public bool TryGetForegroundWindowTitle(out string title)
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
            {
                title = string.Empty;
                return false;
            }

            var length = GetWindowTextLength(handle);
            if (length <= 0)
            {
                title = string.Empty;
                return false;
            }

            var buffer = new StringBuilder(length + 1);
            if (GetWindowText(handle, buffer, buffer.Capacity) <= 0)
            {
                title = string.Empty;
                return false;
            }

            title = buffer.ToString();
            return !string.IsNullOrWhiteSpace(title);
        }

        private const int CursorShowing = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int Size;
            public int Flags;
            public IntPtr CursorHandle;
            public Point ScreenPos;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out WindowRect rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);
    }
}
