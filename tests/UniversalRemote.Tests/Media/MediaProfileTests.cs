using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Profiles;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class MediaProfileTests
{
    [Fact]
    public async Task Access_policy_blocks_age_restricted_and_unrated_content()
    {
        var repository = new MemoryProfileRepository();
        var pins = new MemoryPinStore();
        var service = new MediaProfileService(repository, pins);
        var parent = new MediaProfile(Guid.NewGuid(), "Parents", MediaProfileKind.Adult, isPinProtected: true);
        await service.SaveAsync(parent, "2580");
        var child = new MediaProfile(
            Guid.NewGuid(), "Enfant", MediaProfileKind.Child,
            new MediaProfileRestrictions(maximumAgeRating: 12, blockUnratedContent: true));
        await service.SaveAsync(child);
        Assert.True((await service.SwitchAsync(child.Id)).Succeeded);
        var policy = new MediaProfileAccessPolicy(service);
        var sourceId = Guid.NewGuid();

        var family = new MediaItem(sourceId, "family", MediaItemKind.Movie, "Famille", minimumAge: 7);
        var adult = new MediaItem(sourceId, "adult", MediaItemKind.Movie, "Adulte", minimumAge: 16);
        var unrated = new MediaItem(sourceId, "unknown", MediaItemKind.Movie, "Sans classement");

        Assert.True((await policy.EvaluateAsync(family)).IsAllowed);
        Assert.False((await policy.EvaluateAsync(adult)).IsAllowed);
        Assert.False((await policy.EvaluateAsync(unrated)).IsAllowed);
    }

    [Fact]
    public async Task Protected_profile_requires_correct_pin_before_switch()
    {
        var repository = new MemoryProfileRepository();
        var pins = new MemoryPinStore();
        var service = new MediaProfileService(repository, pins);
        var adult = new MediaProfile(Guid.NewGuid(), "Parents", MediaProfileKind.Adult, isPinProtected: true);
        await service.SaveAsync(adult, "2580");

        var missing = await service.SwitchAsync(adult.Id);
        var wrong = await service.SwitchAsync(adult.Id, "0000");
        var correct = await service.SwitchAsync(adult.Id, "2580");

        Assert.False(missing.Succeeded);
        Assert.True(missing.PinRequired);
        Assert.False(wrong.Succeeded);
        Assert.True(correct.Succeeded);
    }


    [Fact]
    public async Task Child_profile_requires_at_least_one_pin_protected_adult_profile()
    {
        var repository = new MemoryProfileRepository();
        var pins = new MemoryPinStore();
        var service = new MediaProfileService(repository, pins);
        _ = await service.GetActiveAsync();
        var child = new MediaProfile(Guid.NewGuid(), "Enfant", MediaProfileKind.Child,
            new MediaProfileRestrictions(maximumAgeRating: 12, blockUnratedContent: true));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(child));

        var principal = (await service.ListAsync()).Single(x => x.Kind == MediaProfileKind.Adult);
        await service.SaveAsync(new MediaProfile(principal.Id, principal.DisplayName, MediaProfileKind.Adult,
            principal.Restrictions, principal.Preferences, isPinProtected: true), "2580");
        await service.SaveAsync(child);
        Assert.Contains((await service.ListAsync()), x => x.Id == child.Id);
    }

    [Fact]
    public async Task Profile_file_contains_preferences_but_no_pin_or_provider_credentials()
    {
        var path = Path.Combine(Path.GetTempPath(), $"universalremote-profile-{Guid.NewGuid():N}.json");
        try
        {
            var repository = new FileMediaProfileRepository(path);
            var profile = new MediaProfile(
                Guid.NewGuid(), "Invité", MediaProfileKind.Guest,
                new MediaProfileRestrictions(blockedCategories: ["Restricted"]),
                new MediaProfilePreferences("fr", false, true),
                isPinProtected: false);
            await repository.SaveAsync(profile);
            await repository.SetActiveProfileIdAsync(profile.Id);
            var json = await File.ReadAllTextAsync(path);

            using var document = System.Text.Json.JsonDocument.Parse(json);
            Assert.Equal(profile.DisplayName, document.RootElement.GetProperty("profiles")[0].GetProperty("displayName").GetString());
            Assert.Contains("Restricted", json, StringComparison.Ordinal);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("stream", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pinValue", json, StringComparison.OrdinalIgnoreCase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class MemoryProfileRepository : IMediaProfileRepository
    {
        private readonly Dictionary<Guid, MediaProfile> items = [];
        private Guid? active;
        public Task<IReadOnlyList<MediaProfile>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MediaProfile>>(items.Values.ToArray());
        public Task<MediaProfile?> FindAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(items.GetValueOrDefault(id));
        public Task SaveAsync(MediaProfile profile, CancellationToken cancellationToken = default)
        { items[profile.Id] = profile; return Task.CompletedTask; }
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(items.Remove(id));
        public Task<Guid?> GetActiveProfileIdAsync(CancellationToken cancellationToken = default) => Task.FromResult(active);
        public Task SetActiveProfileIdAsync(Guid id, CancellationToken cancellationToken = default)
        { active = id; return Task.CompletedTask; }
    }

    private sealed class MemoryPinStore : IMediaProfilePinStore
    {
        private readonly Dictionary<Guid, string> values = [];
        public Task<bool> HasPinAsync(Guid profileId, CancellationToken cancellationToken = default)
            => Task.FromResult(values.ContainsKey(profileId));
        public Task SetPinAsync(Guid profileId, string pin, CancellationToken cancellationToken = default)
        { values[profileId] = pin; return Task.CompletedTask; }
        public Task<bool> VerifyPinAsync(Guid profileId, string pin, CancellationToken cancellationToken = default)
            => Task.FromResult(values.TryGetValue(profileId, out var value) && value == pin);
        public Task DeletePinAsync(Guid profileId, CancellationToken cancellationToken = default)
        { values.Remove(profileId); return Task.CompletedTask; }
    }
}
