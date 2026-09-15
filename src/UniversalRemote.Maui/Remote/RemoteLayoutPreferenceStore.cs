using UniversalRemote.Theming;
namespace UniversalRemote.Maui.Remote;

public sealed class RemoteLayoutPreferenceStore
{
    private static string Key(Guid deviceId) => $"remote.appearance.{deviceId:N}";
    public RemoteLayoutPreferences Load(Guid deviceId) => RemoteLayoutPreferencesJson.ParseOrDefault(Preferences.Default.Get(Key(deviceId), string.Empty));
    public void Save(Guid deviceId, string layoutId) => Preferences.Default.Set(Key(deviceId), RemoteLayoutPreferencesJson.Serialize(RemoteLayoutPreferences.ForLayout(layoutId)));
}
