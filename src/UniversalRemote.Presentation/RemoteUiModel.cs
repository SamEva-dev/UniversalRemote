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

/// <summary>Immutable UI projection built only from device capabilities.</summary>
public sealed record RemoteUiModel(
    Guid DeviceId,
    string DisplayName,
    IReadOnlyList<RemoteUiSection> Sections)
{
    public IReadOnlyList<RemoteUiControl> Controls => Sections.SelectMany(section => section.Controls).ToArray();
}

public interface IRemoteUiModelBuilder
{
    RemoteUiModel Build(Device device);
}
