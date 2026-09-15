using System.Text.Json;

namespace UniversalRemote.Telemetry;

public sealed class TelemetryExporter(ITelemetryEventStore store, TimeProvider? timeProvider = null) : ITelemetryExporter
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public async ValueTask ExportAsync(string destinationPath, string productVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("An export path is required.", nameof(destinationPath));
        if (string.IsNullOrWhiteSpace(productVersion) || productVersion.Length > 64) throw new ArgumentException("A bounded product version is required.", nameof(productVersion));
        var events = await store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        await using var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, FileOptions.Asynchronous);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("generatedUtc", clock.GetUtcNow());
        writer.WriteString("productVersion", productVersion);
        writer.WriteStartArray("events");
        foreach (var item in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteStartObject();
            writer.WriteString("timestampUtc", item.TimestampUtc);
            writer.WriteString("eventName", item.EventName);
            writer.WriteString("component", item.Component);
            writer.WriteString("outcome", item.Outcome.ToString());
            writer.WriteString("durationBucket", item.DurationBucket.ToString());
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
