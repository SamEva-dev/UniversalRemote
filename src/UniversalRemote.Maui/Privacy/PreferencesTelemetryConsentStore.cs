using Microsoft.Maui.Storage;
using UniversalRemote.Remote.Telemetry;

namespace UniversalRemote.Maui.Privacy;

public sealed class PreferencesTelemetryConsentStore : ITelemetryConsentStore
{
    private const string Key = "privacy.telemetry.enabled.v1";
    public bool IsEnabled
    {
        get => Preferences.Default.Get(Key, false);
        set => Preferences.Default.Set(Key, value);
    }
}
