using System.Text;
using System.Text.Json;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Telemetry;

public sealed class LocalTelemetryEventStore : ITelemetryEventStore
{
    private const int MaxLineBytes = 2048;
    private readonly string path;
    private readonly TelemetryOptions options;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim gate = new(1, 1);

    public LocalTelemetryEventStore(string path, TelemetryOptions? options = null, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A telemetry store path is required.", nameof(path));
        this.path = Path.GetFullPath(path);
        this.options = options ?? new TelemetryOptions();
        this.options.Validate();
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask AppendAsync(TelemetryRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ValidateRecord(record);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var events = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            events.Add(record);
            var cutoff = timeProvider.GetUtcNow() - options.EffectiveMaxAge;
            events = events.Where(x => x.TimestampUtc >= cutoff).TakeLast(options.MaxEvents).ToList();
            await WriteUnsafeAsync(events, cancellationToken).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<TelemetryRecord>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var events = await ReadUnsafeAsync(cancellationToken).ConfigureAwait(false);
            var cutoff = timeProvider.GetUtcNow() - options.EffectiveMaxAge;
            var retained = events.Where(x => x.TimestampUtc >= cutoff).TakeLast(options.MaxEvents).ToArray();
            if (retained.Length != events.Count)
                await WriteUnsafeAsync(retained, cancellationToken).ConfigureAwait(false);
            return retained;
        }
        finally { gate.Release(); }
    }

    public async ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        finally { gate.Release(); }
    }

    private async Task<List<TelemetryRecord>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        var result = new List<TelemetryRecord>();
        if (!File.Exists(path)) return result;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: false);
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line) || Encoding.UTF8.GetByteCount(line) > MaxLineBytes) continue;
            if (TryParse(line, out var record)) result.Add(record!);
        }
        return result;
    }

    private async Task WriteUnsafeAsync(IReadOnlyList<TelemetryRecord> events, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
        await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            foreach (var item in events)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(Serialize(item).AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    internal static string Serialize(TelemetryRecord record)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("timestampUtc", record.TimestampUtc);
            writer.WriteString("eventName", record.EventName);
            writer.WriteString("component", record.Component);
            writer.WriteString("outcome", record.Outcome.ToString());
            writer.WriteString("durationBucket", record.DurationBucket.ToString());
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static bool TryParse(string json, out TelemetryRecord? record)
    {
        record = null;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false, MaxDepth = 8 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 1) return false;
            if (!root.TryGetProperty("timestampUtc", out var timestamp) || !timestamp.TryGetDateTimeOffset(out var when)) return false;
            if (!root.TryGetProperty("eventName", out var nameElement) || !root.TryGetProperty("component", out var componentElement) ||
                !root.TryGetProperty("outcome", out var outcomeElement) || !root.TryGetProperty("durationBucket", out var bucketElement)) return false;
            var name = nameElement.GetString();
            var component = componentElement.GetString();
            var outcomeText = outcomeElement.GetString();
            var bucketText = bucketElement.GetString();
            if (!Enum.TryParse<TelemetryOutcome>(outcomeText, out var outcome) || !Enum.IsDefined(outcome) ||
                !Enum.TryParse<TelemetryDurationBucket>(bucketText, out var bucket) || !Enum.IsDefined(bucket)) return false;
            var candidate = new TelemetryRecord(when, name ?? string.Empty, component ?? string.Empty, outcome, bucket);
            ValidateRecord(candidate);
            record = candidate;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    private static void ValidateRecord(TelemetryRecord record)
    {
        if (!Enum.IsDefined(record.Outcome) || !Enum.IsDefined(record.DurationBucket))
            throw new ArgumentOutOfRangeException(nameof(record));
        _ = TelemetryRecord.Create(record.EventName, record.Component, record.Outcome, BucketRepresentative(record.DurationBucket), record.TimestampUtc);
    }

    private static TimeSpan BucketRepresentative(TelemetryDurationBucket bucket) => bucket switch
    {
        TelemetryDurationBucket.Under50Milliseconds => TimeSpan.Zero,
        TelemetryDurationBucket.Under250Milliseconds => TimeSpan.FromMilliseconds(50),
        TelemetryDurationBucket.Under1Second => TimeSpan.FromMilliseconds(250),
        TelemetryDurationBucket.Under5Seconds => TimeSpan.FromSeconds(1),
        _ => TimeSpan.FromSeconds(5)
    };
}
