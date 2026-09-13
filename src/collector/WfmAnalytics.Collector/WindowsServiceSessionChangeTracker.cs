namespace WfmAnalytics.Collector;

public static class WindowsServiceSessionChangeTracker
{
    public static WindowsSessionEvent FromServiceControlReason(
        int reason,
        int sessionNumber,
        DateTimeOffset at,
        string source = "windows_service_control")
    {
        return new WindowsSessionEvent(
            WindowsSessionProbe.EventTypeFromServiceSessionChange(reason),
            at,
            $"windows-session-{sessionNumber}",
            source,
            $"reason=0x{reason:x};session_number={sessionNumber}");
    }
}
