using System.Text.Json;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Telemetry;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class TelemetryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "universalremote-telemetry-tests", Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(directory, "events.jsonl");

    public TelemetryTests() => Directory.CreateDirectory(directory);
    public void Dispose() { try { Directory.Delete(directory, recursive: true); } catch { } }

    [Fact]
    public async Task Collection_is_disabled_by_default()
    {
        var consent = new InMemoryTelemetryConsentStore();
        var store = new LocalTelemetryEventStore(StorePath);
        var recorder = new TelemetryRecorder(consent, store);
        await recorder.RecordAsync(TelemetryRecord.Create("domainrelay.ListDevices", "application", TelemetryOutcome.Success, TimeSpan.FromMilliseconds(12)));
        Assert.Empty(await store.ReadAsync());
    }

    [Fact]
    public async Task Enabled_collection_persists_only_the_fixed_sanitized_schema()
    {
        var consent = new InMemoryTelemetryConsentStore(true);
        var store = new LocalTelemetryEventStore(StorePath);
        var recorder = new TelemetryRecorder(consent, store);
        await recorder.RecordAsync(TelemetryRecord.Create("domainrelay.ListDevices", "application", TelemetryOutcome.Success, TimeSpan.FromMilliseconds(80)));
        var item = Assert.Single(await store.ReadAsync());
        Assert.Equal("domainrelay.ListDevices", item.EventName);
        Assert.Equal(TelemetryDurationBucket.Under250Milliseconds, item.DurationBucket);
        var raw = await File.ReadAllTextAsync(StorePath);
        foreach (var forbidden in new[] { "ip", "host", "endpoint", "token", "payload", "deviceId", "exceptionMessage" })
            Assert.False(raw.Contains($"\"{forbidden}\"", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Store_keeps_only_the_configured_tail()
    {
        var store = new LocalTelemetryEventStore(StorePath, new TelemetryOptions(MaxEvents: 3));
        for (var i = 0; i < 5; i++)
            await store.AppendAsync(TelemetryRecord.Create($"event.{i}", "tests", TelemetryOutcome.Success, TimeSpan.Zero));
        var items = await store.ReadAsync();
        Assert.Equal(3, items.Count);
        Assert.Equal("event.2", items[0].EventName);
        Assert.Equal("event.4", items[2].EventName);
    }

    [Fact]
    public async Task Events_older_than_retention_are_not_returned()
    {
        var store = new LocalTelemetryEventStore(StorePath, new TelemetryOptions(MaxEvents: 10, MaxAge: TimeSpan.FromDays(7)));
        await store.AppendAsync(TelemetryRecord.Create("old.event", "tests", TelemetryOutcome.Success, TimeSpan.Zero, DateTimeOffset.UtcNow.AddDays(-8)));
        await store.AppendAsync(TelemetryRecord.Create("new.event", "tests", TelemetryOutcome.Success, TimeSpan.Zero));
        var item = Assert.Single(await store.ReadAsync());
        Assert.Equal("new.event", item.EventName);
        Assert.DoesNotContain("old.event", await File.ReadAllTextAsync(StorePath));
    }

    [Fact]
    public async Task Manual_export_is_schema_versioned_and_contains_no_network_or_device_fields()
    {
        var store = new LocalTelemetryEventStore(StorePath);
        await store.AppendAsync(TelemetryRecord.Create("domainrelay.ListDevices", "application", TelemetryOutcome.Success, TimeSpan.FromSeconds(1)));
        var export = Path.Combine(directory, "export.json");
        await new TelemetryExporter(store).ExportAsync(export, "0.12.0-preview.3");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(export));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("0.12.0-preview.3", doc.RootElement.GetProperty("productVersion").GetString());
        var raw = doc.RootElement.GetRawText();
        foreach (var forbidden in new[] { "ipAddress", "host", "endpoint", "token", "payload", "deviceId", "hardwareId", "exceptionMessage" })
            Assert.False(raw.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Clear_removes_all_local_events()
    {
        var store = new LocalTelemetryEventStore(StorePath);
        await store.AppendAsync(TelemetryRecord.Create("event.one", "tests", TelemetryOutcome.Success, TimeSpan.Zero));
        await store.ClearAsync();
        Assert.Empty(await store.ReadAsync());
        Assert.False(File.Exists(StorePath));
    }

    [Theory]
    [InlineData("unsafe token")]
    [InlineData("endpoint=https://192.168.1.2")]
    [InlineData("")]
    public void Telemetry_tokens_reject_free_form_values(string value)
        => Assert.Throws<ArgumentException>(() => TelemetryRecord.Create(value, "tests", TelemetryOutcome.Success, TimeSpan.Zero));

    [Fact]
    public async Task Sanitized_application_behavior_can_record_without_exporting_exception_message()
    {
        var sink = new CapturingRecorder();
        var behavior = new SanitizedDiagnosticsBehavior<ListDevices, IReadOnlyList<DeviceSummary>>(sink);
        await Assert.ThrowsAsync<InvalidOperationException>(() => behavior.Handle(new ListDevices(),
            () => throw new InvalidOperationException("token=super-secret"), CancellationToken.None));
        var item = Assert.Single(sink.Items);
        Assert.Equal(TelemetryOutcome.Failed, item.Outcome);
        Assert.Equal("domainrelay.ListDevices", item.EventName);
        Assert.False(item.ToString().Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class CapturingRecorder : ITelemetryRecorder
    {
        public List<TelemetryRecord> Items { get; } = [];
        public ValueTask RecordAsync(TelemetryRecord record, CancellationToken cancellationToken = default)
        {
            Items.Add(record);
            return ValueTask.CompletedTask;
        }
    }
}
