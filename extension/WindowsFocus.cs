using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal static class WindowsFocus
    {
        private const int SwShow = 5;
        private const int SwRestore = 9;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr window);

        public static void EnsureVegasTimelineForeground(Vegas vegas)
        {
            IntPtr main = vegas.MainWindow.Handle;
            IntPtr timeline = vegas.TimelineWindow.Handle;
            uint expectedPid = (uint)Process.GetCurrentProcess().Id;
            uint ownerPid;
            GetWindowThreadProcessId(main, out ownerPid);
            if (main == IntPtr.Zero || ownerPid != expectedPid)
                throw new InvalidOperationException("VEGAS main-window ownership validation failed.");

            if (IsIconic(main)) ShowWindowAsync(main, SwRestore);
            else if (!IsWindowVisible(main)) ShowWindowAsync(main, SwShow);

            IntPtr foreground = GetForegroundWindow();
            uint foregroundPid;
            uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out foregroundPid);
            uint currentThread = GetCurrentThreadId();
            bool attached = false;
            try
            {
                if (foregroundThread != 0 && foregroundThread != currentThread)
                    attached = AttachThreadInput(currentThread, foregroundThread, true);
                BringWindowToTop(main);
                SetForegroundWindow(main);
                if (timeline != IntPtr.Zero) SetFocus(timeline);
            }
            finally
            {
                if (attached) AttachThreadInput(currentThread, foregroundThread, false);
            }

            Application.DoEvents();
            Thread.Sleep(100);
            foreground = GetForegroundWindow();
            GetWindowThreadProcessId(foreground, out foregroundPid);
            if (foregroundPid != expectedPid)
                throw new InvalidOperationException("VEGAS could not be verified as the foreground application.");
        }
    }
}
