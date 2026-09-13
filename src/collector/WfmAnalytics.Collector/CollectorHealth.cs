using System.Diagnostics;
using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public sealed record TamperResistanceControl(
    [property: JsonPropertyName("control")] string Control,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("evidence")] string Evidence);

public sealed record CollectorHealthReport(
    [property: JsonPropertyName("checked_at")] DateTimeOffset CheckedAt,
    [property: JsonPropertyName("machine_name")] string MachineName,
    [property: JsonPropertyName("user_name")] string UserName,
    [property: JsonPropertyName("process_id")] int ProcessId,
    [property: JsonPropertyName("process_session_id")] string SessionId,
    [property: JsonPropertyName("agent_version")] string AgentVersion,
    [property: JsonPropertyName("queue")] QueueHealthSnapshot Queue,
    [property: JsonPropertyName("session")] WindowsSessionSnapshot? Session,
    [property: JsonPropertyName("tamper_resistance")] IReadOnlyList<TamperResistanceControl> TamperResistance,
    [property: JsonPropertyName("warnings")] IReadOnlyList<string> Warnings);

public static class CollectorHealth
{
    public static CollectorHealthReport Create(string queueRoot, string agentVersion, FileBackedEncryptedQueue queue)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        var session = WindowsSessionProbe.TryGetCurrentSession();
        var controls = BuildTamperResistanceControls(queueRoot);
        var warnings = new List<string>();
        var queueHealth = queue.GetHealth(checkedAt);

        if (!queueHealth.WithinByteLimit || !queueHealth.WithinAgeLimit)
        {
            warnings.Add("local_queue_retention_cap_exceeded");
        }

        if (queueHealth.LossRecordCount > 0)
        {
            warnings.Add("local_queue_loss_manifest_present");
        }

        if (session is null)
        {
            warnings.Add("windows_session_snapshot_unavailable");
        }

        return new CollectorHealthReport(
            checkedAt,
            Environment.MachineName,
            Environment.UserName,
            Environment.ProcessId,
            CollectorRuntimeIdentity.CreateSessionId(),
            agentVersion,
            queueHealth,
            session,
            controls,
            warnings);
    }

    private static IReadOnlyList<TamperResistanceControl> BuildTamperResistanceControls(string queueRoot)
    {
        var executablePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "unknown";
        var controls = new List<TamperResistanceControl>
        {
            new("encrypted_local_queue", "implemented", $"queue_root={Path.GetFullPath(queueRoot)}"),
            new("dpapi_user_bound_key", OperatingSystem.IsWindows() ? "implemented" : "unavailable", WindowsDpapiKeyStore.ProtectionDescription),
            new("acknowledged_payload_purge", "implemented", "collector deletes accepted pending payloads after server acknowledgement"),
            new("loss_manifest", "implemented", "collector records queue retention drops instead of silently discarding payloads"),
            new("service_control_session_change_mapping", "implemented", "WTS session-change reason codes map to auditable event names"),
            new("managed_service_install", "pending_company_deployment", "requires company endpoint tooling or an approved Windows service installer"),
            new("signed_update_enforcement", "pending_company_deployment", "requires company code-signing or internal PKI decision"),
            new("process_protection", "not_planned", "no kernel driver or intrusive anti-tamper mechanism in the first release"),
            new("executable_path", "observed", executablePath)
        };

        return controls;
    }
}
