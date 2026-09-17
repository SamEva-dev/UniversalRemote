using System.Collections.Frozen;

namespace UniversalRemote.Media.Abstractions;

public enum MediaProfileKind
{
    Adult,
    Child,
    Guest
}

/// <summary>Non-secret per-profile Media preferences.</summary>
public sealed record MediaProfilePreferences
{
    public string PreferredLanguage { get; }
    public bool AutoplayNextEpisode { get; }
    public bool ShowSubtitlesByDefault { get; }
    public string? PreferredPlaybackTargetId { get; }

    public MediaProfilePreferences(
        string preferredLanguage = "fr",
        bool autoplayNextEpisode = true,
        bool showSubtitlesByDefault = false,
        string? preferredPlaybackTargetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredLanguage);
        var language = preferredLanguage.Trim().ToLowerInvariant();
        if (language.Length is < 2 or > 12)
            throw new ArgumentOutOfRangeException(nameof(preferredLanguage));
        var target = string.IsNullOrWhiteSpace(preferredPlaybackTargetId) ? null : preferredPlaybackTargetId.Trim();
        if (target?.Length > 200) throw new ArgumentOutOfRangeException(nameof(preferredPlaybackTargetId));

        PreferredLanguage = language;
        AutoplayNextEpisode = autoplayNextEpisode;
        ShowSubtitlesByDefault = showSubtitlesByDefault;
        PreferredPlaybackTargetId = target;
    }
}

/// <summary>
/// Provider-independent restrictions. They are enforced before playback, not only by hiding UI elements.
/// </summary>
public sealed record MediaProfileRestrictions
{
    public IReadOnlySet<MediaItemKind> AllowedKinds { get; }
    public IReadOnlySet<string> BlockedCategories { get; }
    public IReadOnlySet<MediaReference> BlockedItems { get; }
    public int? MaximumAgeRating { get; }
    public bool BlockUnratedContent { get; }

    public MediaProfileRestrictions(
        IEnumerable<MediaItemKind>? allowedKinds = null,
        IEnumerable<string>? blockedCategories = null,
        IEnumerable<MediaReference>? blockedItems = null,
        int? maximumAgeRating = null,
        bool blockUnratedContent = false)
    {
        var kinds = (allowedKinds ?? Enum.GetValues<MediaItemKind>()).ToArray();
        if (kinds.Any(x => !Enum.IsDefined(x))) throw new ArgumentException("Unknown media kind.", nameof(allowedKinds));
        if (maximumAgeRating is < 0 or > 21) throw new ArgumentOutOfRangeException(nameof(maximumAgeRating));

        var categories = (blockedCategories ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (categories.Any(x => x.Length > 150)) throw new ArgumentOutOfRangeException(nameof(blockedCategories));

        var items = (blockedItems ?? []).ToArray();
        if (items.Any(x => x.SourceId == Guid.Empty)) throw new ArgumentException("Blocked media reference is invalid.", nameof(blockedItems));

        AllowedKinds = kinds.ToFrozenSet();
        BlockedCategories = categories.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        BlockedItems = items.ToFrozenSet();
        MaximumAgeRating = maximumAgeRating;
        BlockUnratedContent = blockUnratedContent;
    }

    public static MediaProfileRestrictions Unrestricted { get; } = new();
}

public sealed record MediaProfile
{
    public Guid Id { get; }
    public string DisplayName { get; }
    public MediaProfileKind Kind { get; }
    public MediaProfileRestrictions Restrictions { get; }
    public MediaProfilePreferences Preferences { get; }
    public bool IsPinProtected { get; }

    public MediaProfile(
        Guid id,
        string displayName,
        MediaProfileKind kind,
        MediaProfileRestrictions? restrictions = null,
        MediaProfilePreferences? preferences = null,
        bool isPinProtected = false)
    {
        if (id == Guid.Empty) throw new ArgumentException("Profile ID must not be empty.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var name = displayName.Trim();
        if (name.Length > 80) throw new ArgumentOutOfRangeException(nameof(displayName));

        Id = id;
        DisplayName = name;
        Kind = kind;
        Restrictions = restrictions ?? MediaProfileRestrictions.Unrestricted;
        Preferences = preferences ?? new MediaProfilePreferences();
        IsPinProtected = isPinProtected;
    }
}

public sealed record MediaAccessDecision(bool IsAllowed, string? ReasonCode = null, string? UserMessage = null)
{
    public static MediaAccessDecision Allowed { get; } = new(true);

    public static MediaAccessDecision Denied(string code, string message)
        => new(false, code, message);
}

public sealed record MediaProfileSwitchResult(bool Succeeded, bool PinRequired, string? Message = null)
{
    public static MediaProfileSwitchResult Success { get; } = new(true, false);
}

public interface IMediaProfileRepository
{
    Task<IReadOnlyList<MediaProfile>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaProfile?> FindAsync(Guid id, CancellationToken cancellationToken = default);
    Task SaveAsync(MediaProfile profile, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Guid?> GetActiveProfileIdAsync(CancellationToken cancellationToken = default);
    Task SetActiveProfileIdAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Stores only a verifier for a profile PIN. Implementations must never persist the plaintext PIN.</summary>
public interface IMediaProfilePinStore
{
    Task<bool> HasPinAsync(Guid profileId, CancellationToken cancellationToken = default);
    Task SetPinAsync(Guid profileId, string pin, CancellationToken cancellationToken = default);
    Task<bool> VerifyPinAsync(Guid profileId, string pin, CancellationToken cancellationToken = default);
    Task DeletePinAsync(Guid profileId, CancellationToken cancellationToken = default);
}

public interface IMediaProfileService
{
    Task<MediaProfile> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaProfile>> ListAsync(CancellationToken cancellationToken = default);
    Task<MediaProfile> SaveAsync(MediaProfile profile, string? newPin = null, CancellationToken cancellationToken = default);
    Task<MediaProfileSwitchResult> SwitchAsync(Guid profileId, string? pin = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}

public interface IMediaAccessPolicy
{
    Task<MediaAccessDecision> EvaluateAsync(MediaItem item, CancellationToken cancellationToken = default);
    Task<MediaAccessDecision> EvaluateAsync(
        MediaReference reference,
        string? category = null,
        int? minimumAge = null,
        CancellationToken cancellationToken = default);
}
