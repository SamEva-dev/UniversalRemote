using System.Text.Json;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Profiles;

public sealed class FileMediaProfileRepository : IMediaProfileRepository
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public FileMediaProfileRepository(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public async Task<IReadOnlyList<MediaProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
        return document.Profiles.Select(ToDomain).OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public async Task<MediaProfile?> FindAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return null;
        var document = await ReadAsync(cancellationToken).ConfigureAwait(false);
        var dto = document.Profiles.FirstOrDefault(x => x.Id == id);
        return dto is null ? null : ToDomain(dto);
    }

    public async Task SaveAsync(MediaProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await UpdateAsync(document =>
        {
            var index = document.Profiles.FindIndex(x => x.Id == profile.Id);
            var dto = FromDomain(profile);
            if (index >= 0) document.Profiles[index] = dto;
            else document.Profiles.Add(dto);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) return false;
        var deleted = false;
        await UpdateAsync(document =>
        {
            deleted = document.Profiles.RemoveAll(x => x.Id == id) > 0;
            if (document.ActiveProfileId == id) document.ActiveProfileId = null;
        }, cancellationToken).ConfigureAwait(false);
        return deleted;
    }

    public async Task<Guid?> GetActiveProfileIdAsync(CancellationToken cancellationToken = default)
        => (await ReadAsync(cancellationToken).ConfigureAwait(false)).ActiveProfileId;

    public Task SetActiveProfileIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty) throw new ArgumentException("Profile ID must not be empty.", nameof(id));
        return UpdateAsync(document => document.ActiveProfileId = id, cancellationToken);
    }

    private async Task<ProfileDocument> ReadAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { gate.Release(); }
    }

    private async Task UpdateAsync(Action<ProfileDocument> update, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            update(document);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(document, JsonOptions), cancellationToken).ConfigureAwait(false);
            File.Move(temp, path, true);
        }
        finally { gate.Release(); }
    }

    private async Task<ProfileDocument> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return new ProfileDocument();
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ProfileDocument>(json, JsonOptions) ?? new ProfileDocument();
        }
        catch (JsonException) { return new ProfileDocument(); }
    }

    private static ProfileDto FromDomain(MediaProfile profile) => new()
    {
        Id = profile.Id,
        DisplayName = profile.DisplayName,
        Kind = profile.Kind,
        IsPinProtected = profile.IsPinProtected,
        AllowedKinds = profile.Restrictions.AllowedKinds.ToArray(),
        BlockedCategories = profile.Restrictions.BlockedCategories.ToArray(),
        BlockedItems = profile.Restrictions.BlockedItems.Select(x => new MediaReferenceDto
        {
            SourceId = x.SourceId, ExternalId = x.ExternalId, Kind = x.Kind
        }).ToArray(),
        MaximumAgeRating = profile.Restrictions.MaximumAgeRating,
        BlockUnratedContent = profile.Restrictions.BlockUnratedContent,
        PreferredLanguage = profile.Preferences.PreferredLanguage,
        AutoplayNextEpisode = profile.Preferences.AutoplayNextEpisode,
        ShowSubtitlesByDefault = profile.Preferences.ShowSubtitlesByDefault,
        PreferredPlaybackTargetId = profile.Preferences.PreferredPlaybackTargetId
    };

    private static MediaProfile ToDomain(ProfileDto profile) => new(
        profile.Id,
        profile.DisplayName,
        profile.Kind,
        new MediaProfileRestrictions(
            profile.AllowedKinds ?? Enum.GetValues<MediaItemKind>(),
            profile.BlockedCategories ?? [],
            (profile.BlockedItems ?? []).Select(x => new MediaReference(x.SourceId, x.ExternalId, x.Kind)),
            profile.MaximumAgeRating,
            profile.BlockUnratedContent),
        new MediaProfilePreferences(
            string.IsNullOrWhiteSpace(profile.PreferredLanguage) ? "fr" : profile.PreferredLanguage,
            profile.AutoplayNextEpisode,
            profile.ShowSubtitlesByDefault,
            profile.PreferredPlaybackTargetId),
        profile.IsPinProtected);

    private sealed class ProfileDocument
    {
        public int Version { get; set; } = 1;
        public Guid? ActiveProfileId { get; set; }
        public List<ProfileDto> Profiles { get; set; } = [];
    }

    private sealed class ProfileDto
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public MediaProfileKind Kind { get; set; }
        public bool IsPinProtected { get; set; }
        public MediaItemKind[] AllowedKinds { get; set; } = Enum.GetValues<MediaItemKind>();
        public string[] BlockedCategories { get; set; } = [];
        public MediaReferenceDto[] BlockedItems { get; set; } = [];
        public int? MaximumAgeRating { get; set; }
        public bool BlockUnratedContent { get; set; }
        public string PreferredLanguage { get; set; } = "fr";
        public bool AutoplayNextEpisode { get; set; } = true;
        public bool ShowSubtitlesByDefault { get; set; }
        public string? PreferredPlaybackTargetId { get; set; }
    }

    private sealed class MediaReferenceDto
    {
        public Guid SourceId { get; set; }
        public string ExternalId { get; set; } = string.Empty;
        public MediaItemKind Kind { get; set; }
    }
}
