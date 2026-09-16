namespace UniversalRemote.Theming;

/// <summary>All built-in styles are data, independent of MAUI and device providers.</summary>
public static class BuiltInRemoteStyles
{
    public static readonly RemoteThemeDefinition DefaultTheme = Theme("classic", "Classic", "#ECEDED", "#E3E5E7", "#15191F", "#D42E29", "#FFFFFF", 12, 16);
    public static readonly RemoteLayoutDefinition Classic = Layout("classic", "Classic", ["power", "navigation", "channel", "audio", "media", "extras"]);
    public static readonly RemoteLayoutDefinition Minimal = Layout("minimal", "Minimal", ["power", "channel", "audio", "navigation", "media", "extras"], RemoteVisualDensity.Spacious);
    public static readonly RemoteLayoutDefinition Nova = Layout("nova", "Nova", ["power", "channel", "audio", "navigation", "media", "extras"]);
    public static readonly RemoteLayoutDefinition Elite = Layout("elite", "Elite", ["power", "navigation", "channel", "audio", "media", "extras"]);
    public static readonly RemoteLayoutDefinition Horizon = Layout("horizon", "Horizon", ["navigation", "channel", "power", "audio", "media", "extras"]);
    public static readonly RemoteLayoutDefinition Fusion = Layout("fusion", "Fusion", ["channel", "audio", "navigation", "media", "power", "extras"]);
    public static readonly RemoteLayoutDefinition Neo = Layout("neo", "Neo", ["power", "navigation", "channel", "audio", "media", "extras"]);
    public static IReadOnlyList<RemoteLayoutDefinition> Layouts { get; } = Array.AsReadOnly(new[] { Classic, Minimal, Nova, Elite, Horizon, Fusion, Neo });
    private static readonly IReadOnlyDictionary<string, RemoteThemeDefinition> Themes = new Dictionary<string, RemoteThemeDefinition>(StringComparer.Ordinal)
    {
        ["classic"] = DefaultTheme,
        ["minimal"] = Theme("minimal", "Minimal", "#FFFFFF", "#F0F2F5", "#101820", "#172B4D", "#FFFFFF", 16, 12),
        ["nova"] = Theme("nova", "Nova", "#F8FAFF", "#EDF0F6", "#101A2A", "#2168C8", "#FFFFFF", 12, 24),
        ["elite"] = Theme("elite", "Elite", "#070B0E", "#1B2025", "#F4F6F8", "#D42E29", "#FFFFFF", 14, 16),
        ["horizon"] = Theme("horizon", "Horizon", "#031E30", "#173E55", "#FFFFFF", "#16B4EB", "#002237", 16, 20),
        ["fusion"] = Theme("fusion", "Fusion", "#080E17", "#19232F", "#F4F7FC", "#28BDF2", "#081827", 12, 18),
        ["neo"] = Theme("neo", "Neo", "#090E21", "#182541", "#F5F5FF", "#47A7FF", "#090E21", 14, 32) with
        { Tokens = new(56, 14, 32, RemoteControlShape.Pill) }
    };
    public static RemoteLayoutDefinition Resolve(string? id) => Layouts.FirstOrDefault(x => x.Id == id) ?? Classic;
    public static RemoteThemeDefinition ThemeFor(string? id) => Themes[Resolve(id).Id];
    private static RemoteLayoutDefinition Layout(string id, string name, string[] order, RemoteVisualDensity density = RemoteVisualDensity.Comfortable) =>
        new(id, name, Array.AsReadOnly(order), density, ShowSectionLabels: id is not ("classic" or "minimal"));
    private static RemoteThemeDefinition Theme(string id, string name, string background, string surface, string text, string accent, string onAccent, double spacing, double radius) =>
        new(id, name, new(48, spacing, radius, RemoteControlShape.RoundedRectangle)) { Palette = new(background, surface, text, accent, onAccent) };
}
