using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IPSwitch.Services
{
    /// <summary>
    /// Memory optimizations that work in a portable executable (no NGen, no installer).
    /// </summary>
    /// <remarks>
    /// <b>Working set trimming</b>: when the app is only in the notification area its pages are released to Windows,
    /// so a resident tray app does not keep ~100 MB of WPF memory that nobody is looking at.
    /// Multicore JIT (<c>ProfileOptimization</c>) was measured and discarded: startup did not improve and the
    /// working set grew by ~30 MB.
    /// </remarks>
    public static class PerformanceTuning
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr minimumSize, IntPtr maximumSize);

        /// <summary>
        /// Releases the process working set to Windows. Pages are brought back on demand when the user opens a window.
        /// </summary>
        public static void TrimWorkingSet()
        {
            try
            {
                using var process = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(process.Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Debug.WriteLine($"[PerformanceTuning] Trim: {ex.Message}");
            }
        }
    }
}
