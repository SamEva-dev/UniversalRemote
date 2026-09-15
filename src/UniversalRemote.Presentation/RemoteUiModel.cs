using UniversalRemote.Abstractions;

namespace UniversalRemote.Presentation;

public enum RemoteControlRole
{
    Primary,
    Power,
    Audio,
    Navigation,
    Secondary,
    Extra
}

/// <summary>A provider-agnostic action exposed to a renderer.</summary>
public sealed record RemoteUiControl(
    string Id,
    RemoteAction Action,
    string LabelKey,
    RemoteControlRole Role,
    int Priority);

/// <summary>A semantic section. Renderers decide its concrete visual arrangement.</summary>
public sealed record RemoteUiSection(
    string Id,
    string LabelKey,
    IReadOnlyList<RemoteUiControl> Controls);

/// <summary>Immutable UI projection built from device capabilities. Favorites are shortcut references to existing controls.</summary>
public sealed record RemoteUiModel(
    Guid DeviceId,
    string DisplayName,
    IReadOnlyList<RemoteUiSection> Sections)
{
    public IReadOnlyList<RemoteUiControl> Controls => Sections.SelectMany(section => section.Controls).ToArray();
    public IReadOnlyList<RemoteUiControl> Favorites { get; init; } = Array.Empty<RemoteUiControl>();
}

public interface IRemoteUiModelBuilder
{
    RemoteUiModel Build(Device device);
}

public static class RemoteUiFavoriteProjection
{
    /// <summary>Projects favorite action IDs onto controls already exposed by capabilities. Stale IDs are ignored.</summary>
    public static RemoteUiModel Apply(RemoteUiModel model, IReadOnlyList<string>? favoriteActionIds)
    {
        ArgumentNullException.ThrowIfNull(model);
        var controlsByAction = model.Controls.ToDictionary(control => control.Action.Id, StringComparer.Ordinal);
        var favorites = (favoriteActionIds ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .Where(controlsByAction.ContainsKey)
            .Select(actionId => controlsByAction[actionId])
            .ToArray();
        return model with { Favorites = favorites };
    }
}
