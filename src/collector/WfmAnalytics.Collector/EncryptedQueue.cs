using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public sealed record PendingPayload(Guid QueueId, DateTimeOffset OccurredEndUtc, string CiphertextPath);

public sealed record QueueRetentionOptions(
    [property: JsonPropertyName("max_pending_age")]
    TimeSpan MaxPendingAge,
    [property: JsonPropertyName("max_pending_bytes")]
    long MaxPendingBytes)
{
    public static QueueRetentionOptions Default { get; } = new(TimeSpan.FromDays(7), 100L * 1024L * 1024L);
}

public sealed record QueueLossRecord(
    [property: JsonPropertyName("loss_id")]
    Guid LossId,
    [property: JsonPropertyName("recorded_at")]
    DateTimeOffset RecordedAt,
    [property: JsonPropertyName("lost_from")]
    DateTimeOffset LostFrom,
    [property: JsonPropertyName("lost_to")]
    DateTimeOffset LostTo,
    [property: JsonPropertyName("payload_count")]
    int PayloadCount,
    [property: JsonPropertyName("payload_bytes")]
    long PayloadBytes,
    [property: JsonPropertyName("reason")]
    string Reason);

public sealed record QueueHealthSnapshot(
    [property: JsonPropertyName("checked_at")]
    DateTimeOffset CheckedAt,
    [property: JsonPropertyName("queue_root")]
    string QueueRoot,
    [property: JsonPropertyName("pending_payload_count")]
    int PendingPayloadCount,
    [property: JsonPropertyName("pending_payload_bytes")]
    long PendingPayloadBytes,
    [property: JsonPropertyName("oldest_pending_end_utc")]
    DateTimeOffset? OldestPendingEndUtc,
    [property: JsonPropertyName("newest_pending_end_utc")]
    DateTimeOffset? NewestPendingEndUtc,
    [property: JsonPropertyName("loss_record_count")]
    int LossRecordCount,
    [property: JsonPropertyName("lost_payload_count")]
    int LostPayloadCount,
    [property: JsonPropertyName("lost_payload_bytes")]
    long LostPayloadBytes,
    [property: JsonPropertyName("within_byte_limit")]
    bool WithinByteLimit,
    [property: JsonPropertyName("within_age_limit")]
    bool WithinAgeLimit);

public sealed class AesGcmPayloadProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmPayloadProtector(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key));
        }

        _key = key.ToArray();
    }

    public byte[] Protect(ReadOnlySpan<byte> plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var protectedPayload = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(protectedPayload.AsSpan(0, NonceSize));
        tag.CopyTo(protectedPayload.AsSpan(NonceSize, TagSize));
        ciphertext.CopyTo(protectedPayload.AsSpan(NonceSize + TagSize));
        return protectedPayload;
    }

    public byte[] Unprotect(ReadOnlySpan<byte> protectedPayload)
    {
        if (protectedPayload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Protected payload is too small.");
        }

        var nonce = protectedPayload[..NonceSize];
        var tag = protectedPayload.Slice(NonceSize, TagSize);
        var ciphertext = protectedPayload[(NonceSize + TagSize)..];
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}

public sealed class FileBackedEncryptedQueue
{
    private readonly string _root;
    private readonly AesGcmPayloadProtector _protector;
    private readonly QueueRetentionOptions _retention;

    public FileBackedEncryptedQueue(string root, AesGcmPayloadProtector protector, QueueRetentionOptions? retention = null)
    {
        _root = root;
        _protector = protector;
        _retention = retention ?? QueueRetentionOptions.Default;
        Directory.CreateDirectory(PayloadDirectory);
    }

    private string PayloadDirectory => Path.Combine(_root, "pending");
    private string ManifestPath => Path.Combine(_root, "manifest.jsonl");
    private string LossManifestPath => Path.Combine(_root, "loss-manifest.jsonl");

    public PendingPayload Enqueue(ActivityEnvelope envelope)
    {
        var queueId = Guid.NewGuid();
        var payloadPath = Path.Combine(PayloadDirectory, $"{queueId:N}.bin");
        var json = JsonSerializer.Serialize(envelope, CollectorJson.Options);
        var encrypted = _protector.Protect(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(payloadPath, encrypted);

        var pending = new PendingPayload(queueId, envelope.BucketEnd, payloadPath);
        File.AppendAllText(ManifestPath, JsonSerializer.Serialize(pending, CollectorJson.Options) + Environment.NewLine, Encoding.UTF8);
        EnforceRetention(DateTimeOffset.UtcNow);
        return pending;
    }

    public IReadOnlyList<PendingPayload> ListPending()
    {
        if (!File.Exists(ManifestPath))
        {
            return [];
        }

        return File.ReadLines(ManifestPath, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<PendingPayload>(line, CollectorJson.Options) ?? throw new InvalidOperationException("Invalid queue manifest row."))
            .Where(pending => File.Exists(pending.CiphertextPath))
            .OrderBy(pending => pending.OccurredEndUtc)
            .ToArray();
    }

    public ActivityEnvelope Read(PendingPayload pending)
    {
        var protectedPayload = File.ReadAllBytes(pending.CiphertextPath);
        var plaintext = _protector.Unprotect(protectedPayload);
        return JsonSerializer.Deserialize<ActivityEnvelope>(plaintext, CollectorJson.Options)
            ?? throw new InvalidOperationException("Invalid activity envelope payload.");
    }

    public void Acknowledge(PendingPayload pending)
    {
        File.Delete(pending.CiphertextPath);
        RewriteManifest(ListPending());
    }

    public IReadOnlyList<QueueLossRecord> ListLosses()
    {
        if (!File.Exists(LossManifestPath))
        {
            return [];
        }

        return File.ReadLines(LossManifestPath, Encoding.UTF8)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<QueueLossRecord>(line, CollectorJson.Options) ?? throw new InvalidOperationException("Invalid queue loss manifest row."))
            .OrderBy(loss => loss.RecordedAt)
            .ToArray();
    }

    public QueueHealthSnapshot GetHealth(DateTimeOffset? checkedAt = null)
    {
        var now = checkedAt ?? DateTimeOffset.UtcNow;
        var pending = ListPending();
        var losses = ListLosses();
        var pendingBytes = PendingBytes(pending);
        return new QueueHealthSnapshot(
            now,
            _root,
            pending.Count,
            pendingBytes,
            pending.Count == 0 ? null : pending.Min(item => item.OccurredEndUtc),
            pending.Count == 0 ? null : pending.Max(item => item.OccurredEndUtc),
            losses.Count,
            losses.Sum(item => item.PayloadCount),
            losses.Sum(item => item.PayloadBytes),
            pendingBytes <= _retention.MaxPendingBytes,
            pending.All(item => now - item.OccurredEndUtc <= _retention.MaxPendingAge));
    }

    public void EnforceRetention(DateTimeOffset now)
    {
        var pending = ListPending().ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var dropped = new List<PendingPayload>();
        foreach (var item in pending.Where(item => now - item.OccurredEndUtc > _retention.MaxPendingAge).ToArray())
        {
            pending.Remove(item);
            dropped.Add(item);
        }

        while (PendingBytes(pending) > _retention.MaxPendingBytes && pending.Count > 0)
        {
            var item = pending.OrderBy(payload => payload.OccurredEndUtc).First();
            pending.Remove(item);
            dropped.Add(item);
        }

        if (dropped.Count == 0)
        {
            return;
        }

        var droppedBytes = PendingBytes(dropped);
        foreach (var item in dropped)
        {
            File.Delete(item.CiphertextPath);
        }

        RewriteManifest(pending);
        AppendLoss(now, dropped, droppedBytes);
    }

    private static long PendingBytes(IEnumerable<PendingPayload> pending)
    {
        return pending.Sum(item => File.Exists(item.CiphertextPath) ? new FileInfo(item.CiphertextPath).Length : 0L);
    }

    private void RewriteManifest(IEnumerable<PendingPayload> pending)
    {
        var rows = pending
            .OrderBy(item => item.OccurredEndUtc)
            .Select(item => JsonSerializer.Serialize(item, CollectorJson.Options));
        File.WriteAllLines(ManifestPath, rows, Encoding.UTF8);
    }

    private void AppendLoss(DateTimeOffset now, IReadOnlyList<PendingPayload> dropped, long droppedBytes)
    {
        var loss = new QueueLossRecord(
            Guid.NewGuid(),
            now,
            dropped.Min(item => item.OccurredEndUtc),
            dropped.Max(item => item.OccurredEndUtc),
            dropped.Count,
            droppedBytes,
            "queue_retention_cap");
        File.AppendAllText(LossManifestPath, JsonSerializer.Serialize(loss, CollectorJson.Options) + Environment.NewLine, Encoding.UTF8);
    }
}
