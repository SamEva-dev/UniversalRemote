using UniversalRemote.Media.Abstractions;
using UniversalRemote.Media.Provider.M3U;
using UniversalRemote.Media.Provider.Xtream;

namespace UniversalRemote.Tests.Media;

public sealed class MediaSourceSetupTests
{
    [Fact]
    public void M3u_setup_declares_fields_and_returns_redacted_secret()
    {
        IMediaSourceSetupProvider setup = new M3uSourceSetupProvider();
        Assert.Equal("m3u", setup.ProviderId);
        Assert.Single(setup.Fields);

        var secret = setup.CreateSecret(new Dictionary<string, string>
        {
            ["playlistUrl"] = "https://example.test/list.m3u?token=very-secret"
        });

        Assert.DoesNotContain("very-secret", secret.ToString(), StringComparison.Ordinal);
        Assert.Contains("example.test", secret.Reveal(), StringComparison.Ordinal);
    }

    [Fact]
    public void Xtream_setup_owns_credential_encoding_outside_ui()
    {
        IMediaSourceSetupProvider setup = new XtreamSourceSetupProvider();
        Assert.Equal("xtream", setup.ProviderId);
        Assert.Equal(3, setup.Fields.Count);

        var secret = setup.CreateSecret(new Dictionary<string, string>
        {
            ["server"] = "https://iptv.example.test/",
            ["username"] = "alice",
            ["password"] = "top-secret"
        });

        var decoded = XtreamCredentialCodec.Decode(secret);
        Assert.Equal("alice", decoded.Username);
        Assert.Equal("top-secret", decoded.Password);
        Assert.DoesNotContain("top-secret", secret.ToString(), StringComparison.Ordinal);
    }
}
