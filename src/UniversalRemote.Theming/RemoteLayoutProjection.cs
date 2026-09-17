using UniversalRemote.Remote.Presentation;

namespace UniversalRemote.Remote.Theming;

public static class RemoteLayoutProjection
{
    /// <summary>Reorders sections only. Every capability appears once, including future sections.</summary>
    public static IReadOnlyList<RemoteUiSection> Sections(RemoteUiModel model, RemoteLayoutDefinition layout)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(layout);
        var order = layout.SectionOrder.Distinct(StringComparer.Ordinal).ToList();
        return Array.AsReadOnly(model.Sections.OrderBy(s =>
        {
            var index = order.IndexOf(s.Id);
            return index < 0 ? int.MaxValue : index;
        }).ToArray());
    }
}
