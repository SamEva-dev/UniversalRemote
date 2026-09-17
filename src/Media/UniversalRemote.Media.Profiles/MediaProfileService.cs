using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Profiles;

public sealed class MediaProfileService(
    IMediaProfileRepository repository,
    IMediaProfilePinStore pins) : IMediaProfileService
{
    private readonly SemaphoreSlim mutationGate = new(1, 1);
    private MediaProfile? activeCache;

    public async Task<MediaProfile> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (activeCache is { } cached) return cached;
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        if (activeCache is { } seededCached) return seededCached;
        var activeId = await repository.GetActiveProfileIdAsync(cancellationToken).ConfigureAwait(false);
        if (activeId is { } id)
        {
            var current = await repository.FindAsync(id, cancellationToken).ConfigureAwait(false);
            if (current is not null)
            {
                activeCache = current;
                return current;
            }
        }

        var first = (await repository.ListAsync(cancellationToken).ConfigureAwait(false)).First();
        await repository.SetActiveProfileIdAsync(first.Id, cancellationToken).ConfigureAwait(false);
        activeCache = first;
        return first;
    }

    public async Task<IReadOnlyList<MediaProfile>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        return await repository.ListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<MediaProfile> SaveAsync(MediaProfile profile, string? newPin = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSeededUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var existing = (await repository.ListAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            var hasExistingPin = await pins.HasPinAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            var willHavePin = profile.IsPinProtected && (hasExistingPin || !string.IsNullOrWhiteSpace(newPin));

            if (profile.IsPinProtected && !willHavePin)
                throw new ArgumentException("Un PIN de 4 à 8 chiffres est requis pour protéger ce profil.", nameof(newPin));

            var prospective = existing.Where(x => x.Id != profile.Id).Append(profile).ToArray();
            if (prospective.Any(x => x.Kind is MediaProfileKind.Child or MediaProfileKind.Guest))
            {
                var protectedAdultExists = false;
                foreach (var adult in prospective.Where(x => x.Kind == MediaProfileKind.Adult && x.IsPinProtected))
                {
                    if (adult.Id == profile.Id)
                    {
                        if (willHavePin) { protectedAdultExists = true; break; }
                    }
                    else if (await pins.HasPinAsync(adult.Id, cancellationToken).ConfigureAwait(false))
                    {
                        protectedAdultExists = true;
                        break;
                    }
                }

                if (!protectedAdultExists)
                    throw new InvalidOperationException("Protégez d’abord au moins un profil adulte par PIN avant d’utiliser un profil enfant ou invité.");
            }

            if (profile.IsPinProtected)
            {
                if (!string.IsNullOrWhiteSpace(newPin))
                    await pins.SetPinAsync(profile.Id, newPin, cancellationToken).ConfigureAwait(false);
            }
            else if (hasExistingPin)
            {
                await pins.DeletePinAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            }

            await repository.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
            var activeId = await repository.GetActiveProfileIdAsync(cancellationToken).ConfigureAwait(false);
            if (activeId is null)
            {
                await repository.SetActiveProfileIdAsync(profile.Id, cancellationToken).ConfigureAwait(false);
                activeCache = profile;
            }
            else if (activeId == profile.Id)
            {
                activeCache = profile;
            }
            return profile;
        }
        finally { mutationGate.Release(); }
    }

    public async Task<MediaProfileSwitchResult> SwitchAsync(Guid profileId, string? pin = null, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) throw new ArgumentException("Profile ID must not be empty.", nameof(profileId));
        await EnsureSeededAsync(cancellationToken).ConfigureAwait(false);
        var profile = await repository.FindAsync(profileId, cancellationToken).ConfigureAwait(false);
        if (profile is null) return new MediaProfileSwitchResult(false, false, "Profil introuvable.");

        if (profile.IsPinProtected)
        {
            if (string.IsNullOrWhiteSpace(pin))
                return new MediaProfileSwitchResult(false, true, "PIN requis.");
            if (!await pins.VerifyPinAsync(profile.Id, pin, cancellationToken).ConfigureAwait(false))
                return new MediaProfileSwitchResult(false, true, "PIN incorrect.");
        }

        await repository.SetActiveProfileIdAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        activeCache = profile;
        return MediaProfileSwitchResult.Success;
    }

    public async Task<bool> DeleteAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        if (profileId == Guid.Empty) return false;
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureSeededUnlockedAsync(cancellationToken).ConfigureAwait(false);
            var profiles = (await repository.ListAsync(cancellationToken).ConfigureAwait(false)).ToArray();
            if (profiles.Length <= 1) return false;
            if (await repository.GetActiveProfileIdAsync(cancellationToken).ConfigureAwait(false) == profileId) return false;

            var remaining = profiles.Where(x => x.Id != profileId).ToArray();
            if (remaining.Any(x => x.Kind is MediaProfileKind.Child or MediaProfileKind.Guest))
            {
                var protectedAdultExists = false;
                foreach (var adult in remaining.Where(x => x.Kind == MediaProfileKind.Adult && x.IsPinProtected))
                {
                    if (await pins.HasPinAsync(adult.Id, cancellationToken).ConfigureAwait(false))
                    {
                        protectedAdultExists = true;
                        break;
                    }
                }
                if (!protectedAdultExists) return false;
            }

            var deleted = await repository.DeleteAsync(profileId, cancellationToken).ConfigureAwait(false);
            if (deleted) await pins.DeletePinAsync(profileId, cancellationToken).ConfigureAwait(false);
            return deleted;
        }
        finally { mutationGate.Release(); }
    }

    private async Task EnsureSeededAsync(CancellationToken cancellationToken)
    {
        await mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await EnsureSeededUnlockedAsync(cancellationToken).ConfigureAwait(false); }
        finally { mutationGate.Release(); }
    }

    private async Task EnsureSeededUnlockedAsync(CancellationToken cancellationToken)
    {
        if ((await repository.ListAsync(cancellationToken).ConfigureAwait(false)).Count > 0) return;
        var primary = new MediaProfile(
            Guid.NewGuid(),
            "Principal",
            MediaProfileKind.Adult,
            MediaProfileRestrictions.Unrestricted,
            new MediaProfilePreferences("fr", autoplayNextEpisode: true),
            isPinProtected: false);
        await repository.SaveAsync(primary, cancellationToken).ConfigureAwait(false);
        await repository.SetActiveProfileIdAsync(primary.Id, cancellationToken).ConfigureAwait(false);
        activeCache = primary;
    }
}
