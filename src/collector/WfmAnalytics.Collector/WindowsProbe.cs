namespace WfmAnalytics.Collector;

public static partial class WindowsProbe
{
    public static CollectorObservation Observe(DateTimeOffset nowUtc, TimeSpan monotonicElapsed)
    {
        return Observe(nowUtc, monotonicElapsed, CollectionPolicy.Minimum("minimum-default"));
    }

    public static CollectorObservation Observe(DateTimeOffset nowUtc, TimeSpan monotonicElapsed, CollectionPolicy policy)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The collector probe must be run on a real Windows session for WP03 evidence.");
        }

        var foreground = TryGetForegroundProcessInfo(policy.SensitiveCapture);
        var idle = TryGetTimeSinceLastInput(out var elapsed) ? elapsed : TimeSpan.Zero;
        var isLocked = foreground.ProcessName is null && IsWorkstationLikelyLocked();
        var sensitive = new SensitiveObservation(
            foreground.WindowTitle,
            BrowserUrl: null,
            TypedText: null,
            ScreenshotRef: null,
            ClipboardText: null,
            foreground.FullPath).ApplyPolicy(policy.SensitiveCapture);
        return new CollectorObservation(nowUtc, monotonicElapsed, foreground.ProcessName, isLocked, idle, sensitive);
    }

    private static ForegroundProcessInfo TryGetForegroundProcessInfo(SensitiveCaptureSettings settings)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return ForegroundProcessInfo.Empty;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return ForegroundProcessInfo.Empty;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            var processName = process.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? process.ProcessName
                : process.ProcessName + ".exe";
            var title = settings.WindowTitles ? TryGetWindowTitle(window) : null;
            var fullPath = settings.FullPaths ? TryGetFullPath(process) : null;
            return new ForegroundProcessInfo(processName, title, fullPath);
        }
        catch (ArgumentException)
        {
            return ForegroundProcessInfo.Empty;
        }
        catch (InvalidOperationException)
        {
            return ForegroundProcessInfo.Empty;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return ForegroundProcessInfo.Empty;
        }
    }

    private static string? TryGetFullPath(System.Diagnostics.Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static string? TryGetWindowTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new System.Text.StringBuilder(length + 1);
        var copied = GetWindowText(window, buffer, buffer.Capacity);
        return copied > 0 ? buffer.ToString() : null;
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

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

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

    private sealed record ForegroundProcessInfo(string? ProcessName, string? WindowTitle, string? FullPath)
    {
        public static ForegroundProcessInfo Empty { get; } = new(null, null, null);
    }
}
