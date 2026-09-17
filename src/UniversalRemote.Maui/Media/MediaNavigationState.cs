using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Maui.Media;

/// <summary>Short-lived navigation selection. It stores only normalized media metadata, never resolved streams.</summary>
public sealed class MediaNavigationState
{
    private readonly object gate = new();
    private MediaCatalogEntry? selected;

    public void Select(MediaCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (gate) selected = entry;
    }

    public MediaCatalogEntry? Current
    {
        get { lock (gate) return selected; }
    }
}
