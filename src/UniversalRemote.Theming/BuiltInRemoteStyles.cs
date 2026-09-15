namespace UniversalRemote.Theming;

/// <summary>All built-in styles are data, independent of MAUI and device providers.</summary>
public static class BuiltInRemoteStyles
{
    public static readonly RemoteThemeDefinition DefaultTheme = Theme("classic", "Classic", "#F4F6FA", "#FFFFFF", "#162238", "#2455C5", "#FFFFFF", 12, 16);
    public static readonly RemoteLayoutDefinition Classic = Layout("classic", "Classic", ["power", "navigation", "audio", "extras"]);
    public static readonly RemoteLayoutDefinition Minimal = Layout("minimal", "Minimal", ["power", "audio", "navigation", "extras"], RemoteVisualDensity.Spacious);
    public static readonly RemoteLayoutDefinition Nova = Layout("nova", "Nova", ["power", "audio", "navigation", "extras"]);
    public static readonly RemoteLayoutDefinition Elite = Layout("elite", "Elite", ["power", "navigation", "audio", "extras"]);
    public static readonly RemoteLayoutDefinition Horizon = Layout("horizon", "Horizon", ["navigation", "power", "audio", "extras"]);
    public static readonly RemoteLayoutDefinition Fusion = Layout("fusion", "Fusion", ["audio", "navigation", "power", "extras"]);
    public static readonly RemoteLayoutDefinition Neo = Layout("neo", "Neo", ["power", "navigation", "audio", "extras"]);
    public static IReadOnlyList<RemoteLayoutDefinition> Layouts { get; } = Array.AsReadOnly(new[] { Classic, Minimal, Nova, Elite, Horizon, Fusion, Neo });
    private static readonly IReadOnlyDictionary<string, RemoteThemeDefinition> Themes = new Dictionary<string, RemoteThemeDefinition>(StringComparer.Ordinal)
    {
        ["classic"] = DefaultTheme,
        ["minimal"] = Theme("minimal", "Minimal", "#FFFFFF", "#F0F2F5", "#101820", "#172B4D", "#FFFFFF", 16, 12),
        ["nova"] = Theme("nova", "Nova", "#EFF5FF", "#FFFFFF", "#172B4D", "#215AC4", "#FFFFFF", 12, 24),
        ["elite"] = Theme("elite", "Elite", "#17191E", "#272A32", "#F6F0E4", "#E2C278", "#17191E", 14, 16),
        ["horizon"] = Theme("horizon", "Horizon", "#E8F1F8", "#FFFFFF", "#163A52", "#145D86", "#FFFFFF", 16, 20),
        ["fusion"] = Theme("fusion", "Fusion", "#102C30", "#1C4145", "#F1FAF9", "#80DFD3", "#102C30", 12, 18),
        ["neo"] = Theme("neo", "Neo", "#16142D", "#2A2548", "#F7F3FF", "#C6ADFF", "#16142D", 14, 32) with
        { Tokens = new(56, 14, 32, RemoteControlShape.Pill) }
    };
    public static RemoteLayoutDefinition Resolve(string? id) => Layouts.FirstOrDefault(x => x.Id == id) ?? Classic;
    public static RemoteThemeDefinition ThemeFor(string? id) => Themes[Resolve(id).Id];
    private static RemoteLayoutDefinition Layout(string id, string name, string[] order, RemoteVisualDensity density = RemoteVisualDensity.Comfortable) =>
        new(id, name, Array.AsReadOnly(order), density, ShowSectionLabels: id is not ("classic" or "minimal"));
    private static RemoteThemeDefinition Theme(string id, string name, string background, string surface, string text, string accent, string onAccent, double spacing, double radius) =>
        new(id, name, new(48, spacing, radius, RemoteControlShape.RoundedRectangle)) { Palette = new(background, surface, text, accent, onAccent) };
}
