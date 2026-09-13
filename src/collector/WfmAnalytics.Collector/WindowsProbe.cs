namespace WfmAnalytics.Collector;

public static partial class WindowsProbe
{
    public static CollectorObservation Observe(DateTimeOffset nowUtc, TimeSpan monotonicElapsed)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The collector probe must be run on a real Windows session for WP03 evidence.");
        }

        var foregroundProcessName = TryGetForegroundProcessName();
        var idle = TryGetTimeSinceLastInput(out var elapsed) ? elapsed : TimeSpan.Zero;
        var isLocked = foregroundProcessName is null && IsWorkstationLikelyLocked();
        return new CollectorObservation(nowUtc, monotonicElapsed, foregroundProcessName, isLocked, idle);
    }

    private static string? TryGetForegroundProcessName()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? process.ProcessName
                : process.ProcessName + ".exe";
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool TryGetTimeSinceLastInput(out TimeSpan elapsed)
    {
        var info = new LastInputInfo { CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            elapsed = TimeSpan.Zero;
            return false;
        }

        var tickCount = Environment.TickCount64;
        var idleMs = Math.Max(0, tickCount - info.DwTime);
        elapsed = TimeSpan.FromMilliseconds(idleMs);
        return true;
    }

    private static bool IsWorkstationLikelyLocked()
    {
        var desktop = OpenInputDesktop(0, false, 0x0100);
        if (desktop == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            return !SwitchDesktop(desktop);
        }
        finally
        {
            _ = CloseDesktop(desktop);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SwitchDesktop(IntPtr hDesktop);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    private struct LastInputInfo
    {
        public uint CbSize;
        public uint DwTime;
    }
}
