using UniversalRemote.Remote.Provider.LG;
using Xunit;
namespace UniversalRemote.Remote.Tests;
public sealed class LgResponseValidationTests
{
    [Theory]
    [InlineData("{\"id\":\"1\",\"type\":\"error\",\"error\":\"secret\"}")]
    [InlineData("{\"id\":\"1\",\"type\":\"response\",\"payload\":{\"returnValue\":false}}")]
    public void RejectionIsNeverAccepted(string json)
    {
        var exception = Assert.Throws<InvalidDataException>(() => LgResponseValidation.MatchesSuccessfulResponse(json, "1"));
        Assert.DoesNotContain("secret", exception.Message);
    }
    [Fact]
    public void UnrelatedResponseIsIgnored() => Assert.False(LgResponseValidation.MatchesSuccessfulResponse("{\"id\":\"2\",\"type\":\"response\"}", "1"));
    [Fact]
    public void SuccessfulResponseIsRecognized() => Assert.True(LgResponseValidation.MatchesSuccessfulResponse("{\"id\":\"1\",\"type\":\"response\",\"payload\":{\"returnValue\":true}}", "1"));
    [Theory]
    [InlineData("ws://192.168.1.10:3000/pointer")]
    [InlineData("wss://192.168.1.11:3001/pointer")]
    [InlineData("wss://user:pass@192.168.1.10:3001/pointer")]
    [InlineData("wss://192.168.1.10:3001/pointer#fragment")]
    public void PointerCannotDowngradeOrChangeHost(string path) => Assert.Throws<InvalidDataException>(() => LgResponseValidation.PointerUri(path, "192.168.1.10"));
    [Fact]
    public void SameHostSecurePointerIsAllowed() => Assert.Equal("wss", LgResponseValidation.PointerUri("wss://192.168.1.10:3001/pointer", "192.168.1.10").Scheme);
    [Theory]
    [InlineData("{\"payload\":{}}")][InlineData("{\"payload\":{\"mute\":\"false\"}}")]
    public void MissingMuteIsNotAssumedFalse(string json) => Assert.Throws<InvalidDataException>(() => LgResponseValidation.ReadMute(json));
}
