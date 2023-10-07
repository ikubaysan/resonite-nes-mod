using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;


namespace ResoniteNESApp
{
    internal static class NativeMethods
    {

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        public static IntPtr FindWindowByTitleSubstring(string titleSubstring)
        {
            IntPtr foundWindowHandle = IntPtr.Zero;
            string loweredTitleSubstring = titleSubstring.ToLower(); // Convert the substring to lowercase once outside the loop

            NativeMethods.EnumWindows((hWnd, lParam) => {
                int len = NativeMethods.GetWindowTextLength(hWnd);
                if (len > 0)
                {
                    StringBuilder windowTitle = new StringBuilder(len + 1);
                    NativeMethods.GetWindowText(hWnd, windowTitle, len + 1);
                    if (windowTitle.ToString().ToLower().Contains(loweredTitleSubstring)) // Convert window title to lowercase for comparison
                    {
                        foundWindowHandle = hWnd;
                        return false; // Stop the enumeration
                    }
                }
                return true; // Continue the enumeration
            }, IntPtr.Zero);

            return foundWindowHandle;
        }


    }
}
