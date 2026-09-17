using System.Text.Json;
using System.Text.Json.Serialization;

namespace UniversalRemote.Remote.Theming;

/// <summary>Versioned appearance only. Never stores commands, endpoints, credentials or device state.</summary>
public sealed record RemoteLayoutPreferences(int SchemaVersion, string LayoutId)
{
    public const int CurrentVersion = 1;
    public static RemoteLayoutPreferences Default { get; } = new(CurrentVersion, "classic");
    public static RemoteLayoutPreferences ForLayout(string id) => new(CurrentVersion, BuiltInRemoteStyles.Resolve(id).Id);
}

public static class RemoteLayoutPreferencesJson
{
    public static string Serialize(RemoteLayoutPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.SchemaVersion != RemoteLayoutPreferences.CurrentVersion || BuiltInRemoteStyles.Resolve(preferences.LayoutId).Id != preferences.LayoutId)
            throw new ArgumentException("Unsupported layout preferences.", nameof(preferences));
        return JsonSerializer.Serialize(preferences, PreferencesJsonContext.Default.RemoteLayoutPreferences);
    }
    public static RemoteLayoutPreferences ParseOrDefault(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 2048) return RemoteLayoutPreferences.Default;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return RemoteLayoutPreferences.Default;
            var properties = doc.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 2 || properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != 2)
                return RemoteLayoutPreferences.Default;
            var value = JsonSerializer.Deserialize(json, PreferencesJsonContext.Default.RemoteLayoutPreferences);
            return value is not null && value.SchemaVersion == RemoteLayoutPreferences.CurrentVersion &&
                BuiltInRemoteStyles.Resolve(value.LayoutId).Id == value.LayoutId ? value : RemoteLayoutPreferences.Default;
        }
        catch (JsonException) { return RemoteLayoutPreferences.Default; }
    }
}
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RemoteLayoutPreferences))]
internal partial class PreferencesJsonContext : JsonSerializerContext { }
