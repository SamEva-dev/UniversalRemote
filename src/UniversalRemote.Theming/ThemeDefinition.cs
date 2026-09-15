namespace UniversalRemote.Theming;

public enum RemoteVisualDensity
{
    Spacious,
    Comfortable,
    Compact
}

public enum RemoteControlShape
{
    RoundedRectangle,
    Circle,
    Pill
}

public sealed record RemoteThemeTokens(
    double MinimumTouchTargetDp,
    double SpacingDp,
    double CornerRadiusDp,
    RemoteControlShape DefaultControlShape);

public sealed record RemoteThemeDefinition(
    string Id,
    string DisplayName,
    RemoteThemeTokens Tokens)
{
    public RemoteThemePalette Palette { get; init; } = new("#F4F6FA", "#FFFFFF", "#162238", "#2455C5", "#FFFFFF");
}

/// <summary>
/// Layout metadata is semantic and renderer-neutral. It never contains provider-specific rules.
/// </summary>
public sealed record RemoteLayoutDefinition(
    string Id,
    string DisplayName,
    IReadOnlyList<string> SectionOrder,
    RemoteVisualDensity Density,
    bool ShowSectionLabels);

/// <summary>Opaque RGB colors kept as strings so themes remain platform independent.</summary>
public sealed record RemoteThemePalette(string Background, string Surface, string Text, string Accent, string OnAccent);
