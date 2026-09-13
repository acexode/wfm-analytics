using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WfmAnalytics.Collector;

public sealed record PendingPayload(Guid QueueId, DateTimeOffset OccurredEndUtc, string CiphertextPath);

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

    public FileBackedEncryptedQueue(string root, AesGcmPayloadProtector protector)
    {
        _root = root;
        _protector = protector;
        Directory.CreateDirectory(PayloadDirectory);
    }

    private string PayloadDirectory => Path.Combine(_root, "pending");
    private string ManifestPath => Path.Combine(_root, "manifest.jsonl");

    public PendingPayload Enqueue(ActivityEnvelope envelope)
    {
        var queueId = Guid.NewGuid();
        var payloadPath = Path.Combine(PayloadDirectory, $"{queueId:N}.bin");
        var json = JsonSerializer.Serialize(envelope, CollectorJson.Options);
        var encrypted = _protector.Protect(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(payloadPath, encrypted);

        var pending = new PendingPayload(queueId, envelope.BucketEnd, payloadPath);
        File.AppendAllText(ManifestPath, JsonSerializer.Serialize(pending, CollectorJson.Options) + Environment.NewLine, Encoding.UTF8);
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
    }
}
