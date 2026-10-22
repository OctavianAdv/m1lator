using System;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace M1lator.Boost
{
    /// <summary>
    /// Fullscreen stretch: when the Android window covers the whole monitor, use the full area instead of
    /// letterboxing (e.g. 1280x960 / 4:3 fills a 16:9 screen, like "stretched" competitive setups).
    /// Called by the patched LayoutManager.FixupGuestDisplay_FixAspectRatio; returning false keeps stock behaviour.
    /// Input mapping (mouse, touch, keymapping) scales X and Y from the same area, so controls stay aligned.
    /// Setting: HKLM\SOFTWARE\BlueStacks_bgp64\m1lator  StretchFullscreen  REG_DWORD 1 = on (default), 0 = off
    /// </summary>
    public static class Display
    {
        const string KeyPath = @"SOFTWARE\BlueStacks_bgp64\m1lator";

        public static bool TryStretch(Control window, ref Rectangle area)
        {
            try
            {
                if (window == null || !Enabled()) return false;
                Size client = window.ClientSize;
                Rectangle screen = Screen.FromControl(window).Bounds;
                // only in fullscreen: the window's client area covers the whole monitor (small tolerance for borders)
                if (client.Width < screen.Width - 4 || client.Height < screen.Height - 4) return false;
                area = new Rectangle(0, 0, client.Width, client.Height);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool Enabled()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(KeyPath))
            {
                object v = key == null ? null : key.GetValue("StretchFullscreen");
                return !(v is int) || (int)v != 0;
            }
        }
    }
}
