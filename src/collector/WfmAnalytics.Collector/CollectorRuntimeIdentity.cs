namespace WfmAnalytics.Collector;

public static class CollectorRuntimeIdentity
{
    public static string CreateBootId()
    {
        var bootUtc = DateTimeOffset.UtcNow.AddMilliseconds(-Environment.TickCount64);
        return $"{Environment.MachineName}:{bootUtc:yyyyMMddHHmmss}";
    }

    public static string CreateSessionId()
    {
        return TryGetProcessSessionId(out var sessionId)
            ? $"windows-session-{sessionId}"
            : $"user-{Environment.UserName}";
    }

    private static bool TryGetProcessSessionId(out int sessionId)
    {
        if (!OperatingSystem.IsWindows())
        {
            sessionId = 0;
            return false;
        }

        return ProcessIdToSessionId(Environment.ProcessId, out sessionId);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(int dwProcessId, out int pSessionId);
}
