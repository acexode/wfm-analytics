using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public sealed record WindowsSessionSnapshot(
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("session_number")] int SessionNumber,
    [property: JsonPropertyName("connect_state")] string ConnectState,
    [property: JsonPropertyName("user_name")] string? UserName,
    [property: JsonPropertyName("domain_name")] string? DomainName,
    [property: JsonPropertyName("source")] string Source = "wts");

public static class WindowsSessionProbe
{
    public static WindowsSessionSnapshot? TryGetCurrentSession()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!ProcessIdToSessionId(Environment.ProcessId, out var sessionNumber))
        {
            return null;
        }

        return new WindowsSessionSnapshot(
            $"windows-session-{sessionNumber}",
            sessionNumber,
            QueryConnectState(sessionNumber),
            QueryString(sessionNumber, WtsInfoClass.WTSUserName),
            QueryString(sessionNumber, WtsInfoClass.WTSDomainName));
    }

    public static string EventTypeFromServiceSessionChange(int reason)
    {
        return reason switch
        {
            0x5 => "session_logon",
            0x6 => "session_logoff",
            0x7 => "session_locked",
            0x8 => "session_unlocked",
            0x1 => "session_console_connect",
            0x2 => "session_console_disconnect",
            0x3 => "session_remote_connect",
            0x4 => "session_remote_disconnect",
            _ => "session_change_unknown"
        };
    }

    private static string QueryConnectState(int sessionNumber)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionNumber, WtsInfoClass.WTSConnectState, out var buffer, out var bytes) || buffer == IntPtr.Zero)
        {
            return "unknown";
        }

        try
        {
            if (bytes < sizeof(int))
            {
                return "unknown";
            }

            var state = Marshal.ReadInt32(buffer);
            return state switch
            {
                0 => "active",
                1 => "connected",
                2 => "connect_query",
                3 => "shadow",
                4 => "disconnected",
                5 => "idle",
                6 => "listen",
                7 => "reset",
                8 => "down",
                9 => "init",
                _ => "unknown"
            };
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    private static string? QueryString(int sessionNumber, WtsInfoClass infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, sessionNumber, infoClass, out var buffer, out _) || buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var value = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(int dwProcessId, out int pSessionId);

    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WtsInfoClass wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr pointer);

    private enum WtsInfoClass
    {
        WTSUserName = 5,
        WTSDomainName = 7,
        WTSConnectState = 8
    }
}
