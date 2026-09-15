using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Reflection;
using Microsoft.Maui.Storage;
using UniversalRemote.Telemetry;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Maui.Privacy;

public sealed class PrivacyViewModel(
    ITelemetryConsentStore consent,
    ITelemetryEventStore store,
    ITelemetryExporter exporter) : INotifyPropertyChanged
{
    private string status = string.Empty;
    private int eventCount;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool TelemetryEnabled
    {
        get => consent.IsEnabled;
        set
        {
            if (consent.IsEnabled == value) return;
            consent.IsEnabled = value;
            Status = value
                ? "Diagnostics locaux activés. Aucun envoi automatique."
                : "Diagnostics locaux désactivés.";
            OnPropertyChanged();
        }
    }

    public int EventCount
    {
        get => eventCount;
        private set { if (eventCount != value) { eventCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(EventCountText)); } }
    }

    public string EventCountText => $"Événements locaux conservés : {EventCount}";

    public string Status
    {
        get => status;
        private set { if (status != value) { status = value; OnPropertyChanged(); } }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        EventCount = (await store.ReadAsync(cancellationToken)).Count;
        Status = TelemetryEnabled
            ? "Diagnostics locaux activés. Aucun envoi automatique."
            : "Diagnostics locaux désactivés par défaut.";
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await store.ClearAsync(cancellationToken);
        EventCount = 0;
        Status = "Diagnostics locaux effacés.";
    }

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(FileSystem.CacheDirectory, $"universalremote-diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
        await exporter.ExportAsync(file, ProductVersion(), cancellationToken);
        Status = "Export créé localement. Le partage ne démarre qu'après votre action.";
        return file;
    }

    private static string ProductVersion()
    {
        var value = typeof(TelemetryRecord).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(value)) return "unknown";
        var plus = value.IndexOf('+');
        return plus > 0 ? value[..plus] : value;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
