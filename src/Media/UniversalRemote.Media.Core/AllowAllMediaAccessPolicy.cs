using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Core;

/// <summary>Default policy used when the optional Profiles module is not installed.</summary>
public sealed class AllowAllMediaAccessPolicy : IMediaAccessPolicy
{
    public static AllowAllMediaAccessPolicy Instance { get; } = new();

    public Task<MediaAccessDecision> EvaluateAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MediaAccessDecision.Allowed);
    }

    public Task<MediaAccessDecision> EvaluateAsync(
        MediaReference reference,
        string? category = null,
        int? minimumAge = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(MediaAccessDecision.Allowed);
    }
}
