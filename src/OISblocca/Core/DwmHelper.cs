using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OISblocca.Core
{
    public static class DwmHelper
    {
        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

        public enum WindowCornerPreference
        {
            Default = 0,
            DoNotRound = 1,
            Round = 2,
            RoundSmall = 3
        }

        public static void ApplyWindows11Style(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).EnsureHandle();

                // 1. Abilita modalità scura nativa nella barra del titolo Windows
                int darkMode = 1;
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                // 2. Abilita bordi arrotondati nativi Windows 11 (DWMWCP_ROUND)
                int cornerPreference = (int)WindowCornerPreference.Round;
                DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
            }
            catch
            {
                // Ignora su versioni precedenti di Windows
            }
        }
    }
}
