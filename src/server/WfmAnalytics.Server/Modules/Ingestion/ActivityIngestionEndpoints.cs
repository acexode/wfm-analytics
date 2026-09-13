using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using NpgsqlTypes;
using WfmAnalytics.Server.Database;

namespace WfmAnalytics.Server.Modules.Ingestion;

public static class ActivityIngestionEndpoints
{
    private const string EnrollmentHeader = "X-WFM-Enrollment-Id";
    private static readonly TimeSpan ReceiptRetention = TimeSpan.FromDays(37);

    public static IEndpointRouteBuilder MapActivityIngestionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/activity/batches", async (HttpRequest request, PostgresDatabase database, CancellationToken token) =>
        {
            if (!Guid.TryParse(request.Headers[EnrollmentHeader], out var enrollmentId))
            {
                return Results.BadRequest(new { code = "missing_or_invalid_enrollment" });
            }

            using var body = await JsonDocument.ParseAsync(request.Body, cancellationToken: token);
            var root = body.RootElement;
            if (!TryReadBatch(root, out var batchId, out var events, out var error))
            {
                return Results.BadRequest(new { code = error });
            }

            await using var connection = database.CreateConnection();
            await connection.OpenAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            var outcomes = new List<ActivityOutcome>();
            foreach (var activityEvent in events)
            {
                outcomes.Add(await IngestEventAsync(connection, transaction, enrollmentId, activityEvent, token));
            }

            await transaction.CommitAsync(token);
            return Results.Ok(new ActivityBatchResponse("1.0", batchId, outcomes));
        }).AllowAnonymous();

        return endpoints;
    }

    private static bool TryReadBatch(JsonElement root, out Guid batchId, out ActivityEvent[] events, out string error)
    {
        batchId = Guid.Empty;
        events = [];
        error = "invalid_batch";
        if (!root.TryGetProperty("schema_version", out var schema) || schema.GetString() != "1.0")
        {
            error = "unsupported_schema_version";
            return false;
        }

        if (!root.TryGetProperty("batch_id", out var batchElement) || !Guid.TryParse(batchElement.GetString(), out batchId))
        {
            error = "invalid_batch_id";
            return false;
        }

        if (!root.TryGetProperty("events", out var eventArray) || eventArray.ValueKind != JsonValueKind.Array)
        {
            error = "missing_events";
            return false;
        }

        var parsed = new List<ActivityEvent>();
        foreach (var element in eventArray.EnumerateArray())
        {
            if (!TryReadEvent(element, out var activityEvent, out error))
            {
                return false;
            }

            parsed.Add(activityEvent);
        }

        if (parsed.Count is < 1 or > 200)
        {
            error = "event_count_out_of_range";
            return false;
        }

        events = parsed.ToArray();
        return true;
    }

    private static bool TryReadEvent(JsonElement element, out ActivityEvent activityEvent, out string error)
    {
        activityEvent = default;
        error = "invalid_event";
        if (!TryGuid(element, "event_id", out var eventId)
            || !TryGuid(element, "collector_instance_id", out var collectorInstanceId)
            || !TryString(element, "boot_id", out var bootId)
            || !TryString(element, "session_id", out var sessionId)
            || !TryInt64(element, "sequence", out var sequence)
            || !TryDateTimeOffset(element, "bucket_start", out var bucketStart)
            || !TryDateTimeOffset(element, "bucket_end", out var bucketEnd)
            || !element.TryGetProperty("coarsened", out var coarsenedElement)
            || coarsenedElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || !element.TryGetProperty("slices", out var slices)
            || slices.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        if (sequence < 0 || bucketEnd <= bucketStart || bucketEnd - bucketStart > TimeSpan.FromMinutes(1))
        {
            error = "invalid_event_time";
            return false;
        }

        if (slices.GetArrayLength() is < 1 or > 60)
        {
            error = "invalid_slice_count";
            return false;
        }

        var raw = element.GetRawText();
        activityEvent = new ActivityEvent(eventId, bootId, sessionId, collectorInstanceId, sequence, bucketStart, bucketEnd, coarsenedElement.GetBoolean(), raw, Sha256(raw));
        return true;
    }

    private static async Task<ActivityOutcome> IngestEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid enrollmentId, ActivityEvent activityEvent, CancellationToken token)
    {
        await using (var existing = new NpgsqlCommand("SELECT payload_checksum FROM ingestion.ingestion_receipts WHERE enrollment_id=$1 AND event_id=$2", connection, transaction))
        {
            existing.Parameters.AddWithValue(enrollmentId);
            existing.Parameters.AddWithValue(activityEvent.EventId);
            var checksum = await existing.ExecuteScalarAsync(token) as string;
            if (checksum == activityEvent.PayloadChecksum)
            {
                return new ActivityOutcome(activityEvent.EventId, "already_accepted", null);
            }

            if (checksum is not null)
            {
                return new ActivityOutcome(activityEvent.EventId, "rejected", "event_checksum_conflict");
            }
        }

        await using (var sequence = new NpgsqlCommand("SELECT event_id, payload_checksum FROM ingestion.ingestion_receipts WHERE enrollment_id=$1 AND collector_instance_id=$2 AND sequence=$3", connection, transaction))
        {
            sequence.Parameters.AddWithValue(enrollmentId);
            sequence.Parameters.AddWithValue(activityEvent.CollectorInstanceId);
            sequence.Parameters.AddWithValue(activityEvent.Sequence);
            await using var reader = await sequence.ExecuteReaderAsync(token);
            if (await reader.ReadAsync(token))
            {
                var existingEventId = reader.GetGuid(0);
                var existingChecksum = reader.GetString(1);
                await reader.CloseAsync();
                return existingEventId == activityEvent.EventId && existingChecksum == activityEvent.PayloadChecksum
                    ? new ActivityOutcome(activityEvent.EventId, "already_accepted", null)
                    : new ActivityOutcome(activityEvent.EventId, "rejected", "sequence_conflict");
            }
        }

        await using (var receipt = new NpgsqlCommand("INSERT INTO ingestion.ingestion_receipts(enrollment_id,event_id,collector_instance_id,sequence,payload_checksum,receipt_expires_at) VALUES ($1,$2,$3,$4,$5,$6)", connection, transaction))
        {
            receipt.Parameters.AddWithValue(enrollmentId);
            receipt.Parameters.AddWithValue(activityEvent.EventId);
            receipt.Parameters.AddWithValue(activityEvent.CollectorInstanceId);
            receipt.Parameters.AddWithValue(activityEvent.Sequence);
            receipt.Parameters.AddWithValue(activityEvent.PayloadChecksum);
            receipt.Parameters.AddWithValue(DateTimeOffset.UtcNow.Add(ReceiptRetention));
            await receipt.ExecuteNonQueryAsync(token);
        }

        await using (var insert = new NpgsqlCommand("INSERT INTO ingestion.activity_envelopes(enrollment_id,event_id,boot_id,session_id,collector_instance_id,sequence,bucket_start,bucket_end,coarsened,payload,payload_checksum) VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11)", connection, transaction))
        {
            insert.Parameters.AddWithValue(enrollmentId);
            insert.Parameters.AddWithValue(activityEvent.EventId);
            insert.Parameters.AddWithValue(activityEvent.BootId);
            insert.Parameters.AddWithValue(activityEvent.SessionId);
            insert.Parameters.AddWithValue(activityEvent.CollectorInstanceId);
            insert.Parameters.AddWithValue(activityEvent.Sequence);
            insert.Parameters.AddWithValue(activityEvent.BucketStart);
            insert.Parameters.AddWithValue(activityEvent.BucketEnd);
            insert.Parameters.AddWithValue(activityEvent.Coarsened);
            insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, activityEvent.RawJson);
            insert.Parameters.AddWithValue(activityEvent.PayloadChecksum);
            await insert.ExecuteNonQueryAsync(token);
        }

        return new ActivityOutcome(activityEvent.EventId, "accepted", null);
    }

    private static bool TryGuid(JsonElement element, string property, out Guid value)
    {
        value = Guid.Empty;
        return element.TryGetProperty(property, out var child) && Guid.TryParse(child.GetString(), out value);
    }

    private static bool TryString(JsonElement element, string property, out string value)
    {
        value = "";
        return element.TryGetProperty(property, out var child) && child.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value = child.GetString() ?? "");
    }

    private static bool TryInt64(JsonElement element, string property, out long value)
    {
        value = 0;
        return element.TryGetProperty(property, out var child) && child.TryGetInt64(out value);
    }

    private static bool TryDateTimeOffset(JsonElement element, string property, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(property, out var child) && DateTimeOffset.TryParse(child.GetString(), out value);
    }

    private static string Sha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private readonly record struct ActivityEvent(Guid EventId, string BootId, string SessionId, Guid CollectorInstanceId, long Sequence, DateTimeOffset BucketStart, DateTimeOffset BucketEnd, bool Coarsened, string RawJson, string PayloadChecksum);
    private sealed record ActivityBatchResponse(
        [property: JsonPropertyName("schema_version")] string SchemaVersion,
        [property: JsonPropertyName("batch_id")] Guid BatchId,
        [property: JsonPropertyName("outcomes")] IReadOnlyList<ActivityOutcome> Outcomes);

    private sealed record ActivityOutcome(
        [property: JsonPropertyName("event_id")] Guid EventId,
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("rejection_reason")] string? RejectionReason);
}
