using System.Text.Json;
using UniversalRemote.Maui.Storage;
using UniversalRemote.Remote.Provider.AndroidTv;
using UniversalRemote.Remote.Provider.LG;
using UniversalRemote.Remote.Provider.Samsung;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class CredentialSerializationTests
{
    [Fact]
    public void Source_generated_metadata_preserves_existing_credential_shapes()
    {
        var android = new AndroidTvCredentials("192.168.1.2", "cGZ4", "secret", "ABCD");
        var lg = new LgCredentials("192.168.1.3", "key", "ABCD");
        var samsung = new SamsungCredentials("192.168.1.4", "token", "ABCD");
        Assert.Equal(android, JsonSerializer.Deserialize(JsonSerializer.Serialize(android, CredentialJsonContext.Default.AndroidTvCredentials), CredentialJsonContext.Default.AndroidTvCredentials));
        Assert.Equal(lg, JsonSerializer.Deserialize(JsonSerializer.Serialize(lg, CredentialJsonContext.Default.LgCredentials), CredentialJsonContext.Default.LgCredentials));
        Assert.Equal(samsung, JsonSerializer.Deserialize(JsonSerializer.Serialize(samsung, CredentialJsonContext.Default.SamsungCredentials), CredentialJsonContext.Default.SamsungCredentials));
        Assert.Equal(lg, JsonSerializer.Deserialize("{\"Host\":\"192.168.1.3\",\"ClientKey\":\"key\",\"ServerCertificateSha256\":\"ABCD\"}", CredentialJsonContext.Default.LgCredentials));
    }
}
