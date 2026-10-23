using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace M1lator.Boost
{
    /// <summary>
    /// Replaces InputMapper.HandleRawInput (HD-Player): raw mouse input used while a game captures the mouse.
    /// Stock BlueStacks allocates a buffer per WM_INPUT and forwards every single event to the keymapper, which sends
    /// a touch update into the VM. 1000-8000 Hz mice flood the window thread and the VM, and FPS drops while aiming.
    /// Here: one reusable buffer (no allocation per event). Optional: relative mouse moves summed while more WM_INPUT
    /// messages are already queued (the last one of a burst carries the total; buttons/wheel flush immediately).
    /// Coalescing is OFF by default: the native keymapper turns each raw event into touch movement on its own, and
    /// merged deltas made aim skip/stutter in Standoff 2 (v1.9.0 feedback). Every event is forwarded as in stock BlueStacks.
    /// Setting: HKLM\SOFTWARE\BlueStacks_bgp64\m1lator  CoalesceMouse  REG_DWORD 1 = on, 0/absent = off (default)
    /// </summary>
    public static class RawInput
    {
        const uint RID_INPUT = 0x10000003;
        const uint RIM_TYPEMOUSE = 0;
        const uint WM_INPUT = 0x00FF;
        const uint PM_NOREMOVE = 0x0000;
        const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;

        static readonly int HeaderSize = 8 + 2 * IntPtr.Size;   // RAWINPUTHEADER
        static readonly bool Enabled = ReadEnabled();
        static IntPtr buffer = IntPtr.Zero;
        static int bufferSize;
        static int pendingX, pendingY;

        public static void Handle(IntPtr lParam)
        {
            uint size = 0;
            GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)HeaderSize);
            if (size == 0) return;
            if (size > bufferSize)
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                buffer = Marshal.AllocHGlobal((int)size);
                bufferSize = (int)size;
            }
            if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, (uint)HeaderSize) != size) return;

            if (Enabled && (uint)Marshal.ReadInt32(buffer, 0) == RIM_TYPEMOUSE)
            {
                // RAWMOUSE after the header: usFlags@0, usButtonFlags@4, lLastX@12, lLastY@16
                ushort flags = (ushort)Marshal.ReadInt16(buffer, HeaderSize);
                ushort buttons = (ushort)Marshal.ReadInt16(buffer, HeaderSize + 4);
                int dx = Marshal.ReadInt32(buffer, HeaderSize + 12);
                int dy = Marshal.ReadInt32(buffer, HeaderSize + 16);
                bool relative = (flags & MOUSE_MOVE_ABSOLUTE) == 0;
                if (relative)
                {
                    dx += pendingX; dy += pendingY;
                    pendingX = pendingY = 0;
                    if (buttons == 0 && MoreInputQueued())
                    {
                        pendingX = dx; pendingY = dy;   // merged into the next event of this burst
                        return;
                    }
                    Marshal.WriteInt32(buffer, HeaderSize + 12, dx);
                    Marshal.WriteInt32(buffer, HeaderSize + 16, dy);
                }
            }
            ImapHandleRawInput(buffer);
        }

        static bool MoreInputQueued()
        {
            MSG msg;
            return PeekMessage(out msg, IntPtr.Zero, WM_INPUT, WM_INPUT, PM_NOREMOVE);
        }

        static bool ReadEnabled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_bgp64\m1lator"))
                    return key?.GetValue("CoalesceMouse") is int v && v == 1;
            }
            catch { return false; }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x; public int y;
        }

        [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint filterMin, uint filterMax, uint remove);
        // same native keymapper HD-Player already loaded (identical DllImport)
        [DllImport("HD-Imap-Native.dll")] static extern int ImapHandleRawInput(IntPtr buffer);
    }

    /// <summary>
    /// Key auto-repeat filter for VMWindow.UpdateKeyState: a held mapped key already counts as pressed in the keymapper,
    /// so Windows' ~30/s repeated key-downs are dropped. Release and typing are unaffected.
    /// Setting: m1lator\SuppressKeyRepeat REG_DWORD 1 = on (default)
    /// </summary>
    public static class KeyRepeat
    {
        public static readonly bool Suppress = Read();

        public static bool IsSuppressed() => Suppress;

        static bool Read()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\BlueStacks_bgp64\m1lator"))
                    return !(key?.GetValue("SuppressKeyRepeat") is int v) || v != 0;
            }
            catch { return true; }
        }
    }
}
