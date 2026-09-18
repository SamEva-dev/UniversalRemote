using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Core;

namespace UniversalRemote.Tests.Media;

public sealed class MediaConfigurationStoreTests
{
    [Fact]
    public async Task Persists_media_and_epg_metadata_without_secret_values()
    {
        var directory = Path.Combine(Path.GetTempPath(), "universalremote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "media-sources-v1.json");
        try
        {
            using var store = new FileMediaConfigurationStore(path);
            var sourceRepository = (IMediaSourceRepository)store;
            var epgRepository = (IEpgSourceRepository)store;

            var source = new MediaSource(Guid.NewGuid(), "Maison", "m3u", "source:opaque:1");
            var epg = new EpgSource(Guid.NewGuid(), source.Id, "Guide", "xmltv", "epg:opaque:1");

            await sourceRepository.SaveAsync(source);
            await epgRepository.SaveAsync(epg);

            var sources = await sourceRepository.ListAsync();
            var guides = await epgRepository.ListForMediaSourceAsync(source.Id);
            Assert.Single(sources);
            Assert.Single(guides);
            Assert.Equal("source:opaque:1", sources[0].CredentialReference);
            Assert.Equal("epg:opaque:1", guides[0].CredentialReference);

            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("http://", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Deleting_media_source_removes_attached_epg_metadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "universalremote-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "media-sources-v1.json");
        try
        {
            using var store = new FileMediaConfigurationStore(path);
            var sourceRepository = (IMediaSourceRepository)store;
            var epgRepository = (IEpgSourceRepository)store;
            var source = new MediaSource(Guid.NewGuid(), "Maison", "m3u", "source:opaque:2");
            await sourceRepository.SaveAsync(source);
            await epgRepository.SaveAsync(new EpgSource(Guid.NewGuid(), source.Id, "Guide", "xmltv", "epg:opaque:2"));

            Assert.True(await sourceRepository.DeleteAsync(source.Id));
            Assert.Empty(await epgRepository.ListForMediaSourceAsync(source.Id));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
