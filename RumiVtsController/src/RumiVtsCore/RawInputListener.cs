using System.Runtime.InteropServices;

namespace RumiVtsController
{
    internal sealed class RawInputListener : NativeWindow, IDisposable
    {
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIM_TYPEMOUSE = 0;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_REMOVE = 0x00000001;

        private int _accumX;
        private int _accumY;
        private bool _enabled;
        private byte[] _buffer = Array.Empty<byte>();

        public RawInputListener(bool enabled)
        {
            CreateHandle(new CreateParams());
            SetEnabled(enabled);
        }

        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled)
            {
                return;
            }

            _enabled = enabled;
            RegisterMouse(_enabled);
        }

        public bool TryConsumeDelta(out int dx, out int dy)
        {
            dx = Interlocked.Exchange(ref _accumX, 0);
            dy = Interlocked.Exchange(ref _accumY, 0);
            return dx != 0 || dy != 0;
        }

        protected override void WndProc(ref Message m)
        {
            if (_enabled && m.Msg == WM_INPUT)
            {
                ReadRawInput(m.LParam);
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            SetEnabled(false);
            DestroyHandle();
        }

        private void RegisterMouse(bool enable)
        {
            var device = new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x02,
                Flags = enable ? RIDEV_INPUTSINK : RIDEV_REMOVE,
                Target = enable ? Handle : IntPtr.Zero
            };

            RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf<RawInputDevice>());
        }

        private void ReadRawInput(IntPtr handle)
        {
            uint size = 0;
            if (GetRawInputData(handle, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RawInputHeader>()) != 0)
            {
                return;
            }

            if (size == 0)
            {
                return;
            }

            if (_buffer.Length < size)
            {
                _buffer = new byte[size];
            }

            var bufferHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            try
            {
                var bufferPtr = bufferHandle.AddrOfPinnedObject();
                var read = GetRawInputData(handle, RID_INPUT, bufferPtr, ref size, (uint)Marshal.SizeOf<RawInputHeader>());
                if (read != size)
                {
                    return;
                }

                var raw = Marshal.PtrToStructure<RawInput>(bufferPtr);
                if (raw.Header.Type != RIM_TYPEMOUSE)
                {
                    return;
                }

                Interlocked.Add(ref _accumX, raw.Mouse.LastX);
                Interlocked.Add(ref _accumY, raw.Mouse.LastY);
            }
            finally
            {
                bufferHandle.Free();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr WParam;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RawMouse
        {
            [FieldOffset(0)]
            public ushort Flags;
            [FieldOffset(4)]
            public uint Buttons;
            [FieldOffset(4)]
            public ushort ButtonFlags;
            [FieldOffset(6)]
            public ushort ButtonData;
            [FieldOffset(8)]
            public uint RawButtons;
            [FieldOffset(12)]
            public int LastX;
            [FieldOffset(16)]
            public int LastY;
            [FieldOffset(20)]
            public uint ExtraInformation;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInput
        {
            public RawInputHeader Header;
            public RawMouse Mouse;
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint numDevices, uint size);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint sizeHeader);
    }
}
