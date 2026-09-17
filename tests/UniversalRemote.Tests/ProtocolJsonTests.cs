using System.Text.Json;
using UniversalRemote.Remote.Provider.LG;
using UniversalRemote.Remote.Provider.Samsung;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public class ProtocolJsonTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("token\"\\\nété")]
    public void Registration_preserves_manifest_and_escaped_client_key(string? key)
    {
        using var doc = JsonDocument.Parse(LgProtocol.Register(key));
        var root = doc.RootElement;
        Assert.Equal("register", root.GetProperty("type").GetString());
        Assert.Equal("register_0", root.GetProperty("id").GetString());
        var payload = root.GetProperty("payload");
        Assert.False(payload.GetProperty("forcePairing").GetBoolean());
        Assert.Equal("PROMPT", payload.GetProperty("pairingType").GetString());
        Assert.Equal(key, payload.GetProperty("client-key").GetString());
        var manifest = payload.GetProperty("manifest");
        Assert.Equal(1, manifest.GetProperty("manifestVersion").GetInt32());
        Assert.Equal(18, manifest.GetProperty("signed").GetProperty("permissions").GetArrayLength());
        Assert.Equal(5, manifest.GetProperty("permissions").GetArrayLength());
        Assert.Equal("UniversalRemote", manifest.GetProperty("signed").GetProperty("localizedAppNames").GetProperty("").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public void Lg_requests_distinguish_empty_payload_and_boolean_mute(bool? mute)
    {
        using var doc = JsonDocument.Parse(LgProtocol.Request("id\"", "ssap://audio/setMute", mute));
        Assert.Equal("id\"", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("request", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("ssap://audio/setMute", doc.RootElement.GetProperty("uri").GetString());
        var payload = doc.RootElement.GetProperty("payload");
        if (mute is { } value) Assert.Equal(value, payload.GetProperty("mute").GetBoolean());
        else Assert.Empty(payload.EnumerateObject());
    }

    [Theory]
    [InlineData("KEY_POWER")]
    [InlineData("KEY_\"\\\nété")]
    public void Samsung_preserves_case_and_string_option(string key)
    {
        using var doc = JsonDocument.Parse(SamsungProtocol.KeyMessage(key));
        Assert.Equal("ms.remote.control", doc.RootElement.GetProperty("method").GetString());
        var payload = doc.RootElement.GetProperty("params");
        Assert.Equal("Click", payload.GetProperty("Cmd").GetString());
        Assert.Equal(key, payload.GetProperty("DataOfCmd").GetString());
        Assert.Equal("false", payload.GetProperty("Option").GetString());
        Assert.Equal("SendRemoteKey", payload.GetProperty("TypeOfRemote").GetString());
    }
}
