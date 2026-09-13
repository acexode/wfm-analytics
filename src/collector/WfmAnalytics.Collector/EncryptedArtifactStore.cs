using System.Text.Json;
using System.Text.Json.Serialization;

namespace WfmAnalytics.Collector;

public sealed record StoredArtifact(
    [property: JsonPropertyName("artifact_id")] Guid ArtifactId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("ciphertext_path")] string CiphertextPath,
    [property: JsonPropertyName("original_extension")] string OriginalExtension);

public sealed record ArtifactExportResult(
    [property: JsonPropertyName("exported_count")] int ExportedCount,
    [property: JsonPropertyName("output_directory")] string OutputDirectory,
    [property: JsonPropertyName("files")] IReadOnlyList<string> Files);

public sealed class EncryptedArtifactStore
{
    private readonly string _root;
    private readonly AesGcmPayloadProtector _protector;

    public EncryptedArtifactStore(string queueRoot, AesGcmPayloadProtector protector)
    {
        _root = Path.Combine(queueRoot, "artifacts");
        _protector = protector;
        Directory.CreateDirectory(PayloadDirectory);
    }

    private string PayloadDirectory => Path.Combine(_root, "pending");
    private string ManifestPath => Path.Combine(_root, "manifest.jsonl");

    public StoredArtifact Store(string kind, DateTimeOffset occurredAt, string contentType, string extension, ReadOnlySpan<byte> plaintext)
    {
        var artifact = new StoredArtifact(
            Guid.NewGuid(),
            kind,
            occurredAt,
            contentType,
            Path.Combine(PayloadDirectory, $"{Guid.NewGuid():N}.bin"),
            extension.StartsWith(".", StringComparison.Ordinal) ? extension : "." + extension);
        File.WriteAllBytes(artifact.CiphertextPath, _protector.Protect(plaintext));
        File.AppendAllText(ManifestPath, JsonSerializer.Serialize(artifact, CollectorJson.Options) + Environment.NewLine);
        return artifact;
    }

    public IReadOnlyList<StoredArtifact> List()
    {
        if (!File.Exists(ManifestPath))
        {
            return [];
        }

        return File.ReadLines(ManifestPath)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<StoredArtifact>(line, CollectorJson.Options) ?? throw new InvalidOperationException("Invalid artifact manifest row."))
            .Where(artifact => File.Exists(artifact.CiphertextPath))
            .OrderBy(artifact => artifact.OccurredAt)
            .ToArray();
    }

    public byte[] Read(StoredArtifact artifact)
    {
        return _protector.Unprotect(File.ReadAllBytes(artifact.CiphertextPath));
    }

    public ArtifactExportResult ExportAll(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var files = new List<string>();
        foreach (var artifact in List())
        {
            var outputPath = Path.Combine(outputDirectory, $"{artifact.OccurredAt:yyyyMMdd-HHmmss-fff}-{artifact.ArtifactId:N}{artifact.OriginalExtension}");
            File.WriteAllBytes(outputPath, Read(artifact));
            files.Add(Path.GetFullPath(outputPath));
        }

        return new ArtifactExportResult(files.Count, Path.GetFullPath(outputDirectory), files);
    }
}
