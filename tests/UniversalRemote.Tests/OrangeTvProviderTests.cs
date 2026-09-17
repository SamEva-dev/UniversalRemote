using System.Net;
using System.Text;
using UniversalRemote.Remote.Abstractions;
using UniversalRemote.Remote.Application;
using UniversalRemote.Remote.Provider.OrangeTv;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class OrangeTvProviderTests
{
    [Theory]
    [InlineData("192.168.1.30")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.10")]
    [InlineData("169.254.10.20")]
    public void Endpoint_accepts_literal_local_addresses(string address)
    {
        Assert.True(OrangeTvEndpoint.TryNormalizeLocalAddress(address, out var normalized));
        Assert.False(string.IsNullOrWhiteSpace(normalized));
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("8.8.8.8")]
    [InlineData("https://192.168.1.30")]
    [InlineData("orange.example.com")]
    public void Endpoint_rejects_loopback_public_urls_and_hostnames(string address)
        => Assert.False(OrangeTvEndpoint.TryNormalizeLocalAddress(address, out _));

    [Fact]
    public void Route_exposes_only_normalized_verified_actions()
    {
        var route = OrangeTvRemoteProvider.CreateRoute("192.168.1.30");
        Assert.Equal(OrangeTvRemoteProvider.ProviderId, route.ProviderId);
        Assert.Contains(RemoteActions.PowerToggle, route.Capabilities);
        Assert.Contains(RemoteActions.ChannelUp, route.Capabilities);
        Assert.Contains(RemoteActions.PlayPause, route.Capabilities);
        Assert.Contains(RemoteActions.Menu, route.Capabilities);
        Assert.DoesNotContain(RemoteActions.Home, route.Capabilities);
    }

    [Fact]
    public async Task Power_uses_fixed_local_endpoint_and_never_user_controlled_path()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var provider = new OrangeTvRemoteProvider(new HttpClient(handler));

        var result = await provider.ExecuteAsync(
            OrangeTvRemoteProvider.CreateRoute("192.168.1.42"),
            RemoteActions.PowerToggle,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(DeliveryState.Accepted, result.Delivery);
        Assert.Equal("http://192.168.1.42:8080/remoteControl/cmd?operation=01&key=116&mode=0", handler.LastUri);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Non_success_http_result_is_unknown_and_is_not_retried()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var provider = new OrangeTvRemoteProvider(new HttpClient(handler));

        var result = await provider.ExecuteAsync(
            OrangeTvRemoteProvider.CreateRoute("192.168.1.42"),
            RemoteActions.VolumeUp,
            CancellationToken.None);

        Assert.Equal(RemoteErrorCode.ProviderFailure, result.Error);
        Assert.Equal(DeliveryState.Unknown, result.Delivery);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Pairing_requires_an_orange_decoder_status_before_registering_route()
    {
        const string json = """
            {"result":{"responseCode":"0","message":"ok","data":{"friendlyName":"Decodeur TV UHD","macAddress":"AA:BB:CC:DD:EE:FF","activeStandbyState":"0"}}}
            """;
        var handler = new RecordingHandler(_ => JsonResponse(json));
        var provider = new OrangeTvPairingProvider(new HttpClient(handler));
        var candidate = Assert.IsType<PairingCandidate>(provider.CreateManualCandidate("192.168.1.50"));

        var challenge = await provider.StartAsync(candidate, CancellationToken.None);
        var completion = await provider.CompleteAsync(challenge.Id, string.Empty, CancellationToken.None);

        Assert.Equal("Decodeur TV UHD", completion.DisplayName);
        Assert.Equal(OrangeTvRemoteProvider.ProviderId, completion.Route.ProviderId);
        Assert.Equal("192.168.1.50", completion.Route.DeviceKey);
        Assert.Equal("http://192.168.1.50:8080/remoteControl/cmd?operation=10", handler.LastUri);
    }

    [Fact]
    public async Task Pairing_rejects_unrelated_local_http_endpoint()
    {
        const string json = """
            {"result":{"responseCode":"0","message":"ok","data":{"friendlyName":"Other HTTP device"}}}
            """;
        var handler = new RecordingHandler(_ => JsonResponse(json));
        var provider = new OrangeTvPairingProvider(new HttpClient(handler));
        var candidate = Assert.IsType<PairingCandidate>(provider.CreateManualCandidate("192.168.1.60"));
        var challenge = await provider.StartAsync(candidate, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CompleteAsync(challenge.Id, string.Empty, CancellationToken.None));
    }

    [Fact]
    public void Discovery_match_requires_orange_identity_and_local_address()
    {
        var provider = new OrangeTvPairingProvider(new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        var match = provider.Match(new PairingProbe("Décodeur TV UHD Orange", ["192.168.1.70"], ["_http._tcp.local"]));
        Assert.NotNull(match);
        Assert.Equal(OrangeTvRemoteProvider.ProviderId, match!.ProviderId);

        var unrelated = provider.Match(new PairingProbe("Printer", ["192.168.1.71"], ["_http._tcp.local"]));
        Assert.Null(unrelated);
    }

    [Fact]
    public async Task Manual_pairing_query_is_provider_agnostic()
    {
        IManualPairingProvider provider = new OrangeTvPairingProvider(new HttpClient(new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK))));
        var handler = new GetManualPairingCandidatesHandler([provider]);

        var candidates = await handler.Handle(new GetManualPairingCandidates("192.168.1.80"), CancellationToken.None);

        var candidate = Assert.Single(candidates);
        Assert.Equal(OrangeTvRemoteProvider.ProviderId, candidate.ProviderId);
        Assert.Equal("192.168.1.80", candidate.DeviceKey);
    }

    [Fact]
    public async Task Status_parser_rejects_error_response_code()
    {
        const string json = """
            {"result":{"responseCode":"-1","message":"error","data":{}}}
            """;
        using var response = JsonResponse(json);
        Assert.Null(await OrangeTvProtocol.ReadStatusAsync(response, CancellationToken.None));
    }

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastUri = request.RequestUri?.AbsoluteUri;
            return Task.FromResult(responseFactory(request));
        }
    }
}
