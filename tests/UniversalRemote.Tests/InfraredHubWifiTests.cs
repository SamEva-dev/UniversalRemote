using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Abstractions;
using UniversalRemote.Discovery;
using UniversalRemote.Hub.Wifi;
using Xunit;

namespace UniversalRemote.Tests;

public sealed class InfraredHubWifiTests
{
    private static readonly InfraredHubId HubId = new("living-room-hub");

    [Fact]
    public void Default_discovery_requests_the_dedicated_hub_mdns_service()
    {
        Assert.Contains(WifiInfraredHubProtocol.MdnsServiceType, new DiscoveryScanOptions().MdnsServiceTypes);
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    public void Connection_rejects_non_local_addresses(string address)
    {
        Assert.Throws<ArgumentException>(() => new WifiInfraredHubConnection(HubId, address, 8081, 1));
    }

    [Theory]
    [InlineData("192.168.1.44")]
    [InlineData("10.0.0.8")]
    [InlineData("172.20.1.4")]
    [InlineData("169.254.1.8")]
    [InlineData("fd00::1234")]
    [InlineData("fe80::1234")]
    public void Connection_accepts_private_or_link_local_addresses(string address)
    {
        var connection = new WifiInfraredHubConnection(HubId, address, 8081, 1);
        Assert.False(string.IsNullOrWhiteSpace(connection.Address));
    }

    [Fact]
    public async Task Discovery_returns_sanitized_advertisement_and_caches_endpoint_only_in_transport_layer()
    {
        var deviceDiscovery = new FakeDeviceDiscovery([HubDevice("192.168.1.44")]);
        var cache = new WifiInfraredHubCandidateCache();
        var discovery = new WifiInfraredHubDiscovery(deviceDiscovery, cache);

        var result = await discovery.DiscoverAsync(TimeSpan.FromMilliseconds(250));

        var hub = Assert.Single(result);
        Assert.Equal(HubId, hub.Id);
        Assert.Equal("Salon IR", hub.DisplayName);
        Assert.Equal("1.2.3", hub.FirmwareVersion);
        Assert.Equal(InfraredHubTransportKind.Wifi, hub.TransportKind);
        Assert.False(hub.DiagnosticCode.Contains("192.168", StringComparison.Ordinal));
        Assert.True(cache.TryGet(HubId, out var endpoint));
        Assert.Equal("192.168.1.44", endpoint!.Address);
        Assert.Equal(8081, endpoint.Port);
    }

    [Fact]
    public async Task Discovery_skips_duplicate_hub_id_pointing_to_conflicting_endpoints()
    {
        var discovery = new WifiInfraredHubDiscovery(
            new FakeDeviceDiscovery([HubDevice("192.168.1.44"), HubDevice("192.168.1.45")]),
            new WifiInfraredHubCandidateCache());

        Assert.Empty(await discovery.DiscoverAsync());
    }

    [Fact]
    public async Task Connector_validates_identity_then_persists_endpoint_and_returns_generic_hub_client()
    {
        var cache = new WifiInfraredHubCandidateCache();
        cache.Set(new WifiInfraredHubConnection(HubId, "192.168.1.44", 8081, 1));
        var store = new InMemoryWifiInfraredHubConnectionStore();
        var api = Api(InfoJson(HubId.Value));
        var transport = new WifiInfraredHubTransport(store, api, TransmitClient());
        var connector = new WifiInfraredHubConnector(cache, store, api, transport);

        var result = await connector.ConnectAsync(HubId);

        Assert.Equal(InfraredHubConnectOutcome.Connected, result.Outcome);
        Assert.NotNull(result.Hub);
        Assert.NotNull(result.Info);
        Assert.Equal(InfraredHubTransportKind.Wifi, result.Hub!.TransportKind);
        Assert.Equal(InfraredHubConnectionState.Ready, result.Info!.State);
        Assert.NotNull(await store.FindAsync(HubId));
    }

    [Fact]
    public async Task Identity_mismatch_is_rejected_and_never_persisted()
    {
        var cache = new WifiInfraredHubCandidateCache();
        cache.Set(new WifiInfraredHubConnection(HubId, "192.168.1.44", 8081, 1));
        var store = new InMemoryWifiInfraredHubConnectionStore();
        var api = Api(InfoJson("different-hub"));
        var connector = new WifiInfraredHubConnector(cache, store, api, new WifiInfraredHubTransport(store, api, TransmitClient()));

        var result = await connector.ConnectAsync(HubId);

        Assert.Equal(InfraredHubConnectOutcome.IdentityMismatch, result.Outcome);
        Assert.Null(await store.FindAsync(HubId));
    }

    [Fact]
    public async Task Unsupported_api_version_is_rejected_before_persistence()
    {
        var cache = new WifiInfraredHubCandidateCache();
        cache.Set(new WifiInfraredHubConnection(HubId, "192.168.1.44", 8081, 1));
        var store = new InMemoryWifiInfraredHubConnectionStore();
        var api = Api(InfoJson(HubId.Value, schemaVersion: 2));
        var connector = new WifiInfraredHubConnector(cache, store, api, new WifiInfraredHubTransport(store, api, TransmitClient()));

        var result = await connector.ConnectAsync(HubId);

        Assert.Equal(InfraredHubConnectOutcome.UnsupportedApiVersion, result.Outcome);
        Assert.Null(await store.FindAsync(HubId));
    }

    [Fact]
    public async Task Persisted_connection_can_be_revalidated_after_discovery_cache_is_gone()
    {
        var store = new InMemoryWifiInfraredHubConnectionStore();
        await store.SaveAsync(new WifiInfraredHubConnection(HubId, "192.168.1.44", 8081, 1));
        var api = Api(InfoJson(HubId.Value));
        var connector = new WifiInfraredHubConnector(new WifiInfraredHubCandidateCache(), store, api, new WifiInfraredHubTransport(store, api, TransmitClient()));

        var result = await connector.ConnectAsync(HubId);

        Assert.Equal(InfraredHubConnectOutcome.Connected, result.Outcome);
        Assert.Equal("hub.wifi.connected", result.DiagnosticCode);
    }

    [Fact]
    public async Task Transport_info_is_sanitized_and_learning_remains_unsupported_in_remote_048()
    {
        var store = new InMemoryWifiInfraredHubConnectionStore();
        await store.SaveAsync(new WifiInfraredHubConnection(HubId, "192.168.1.44", 8081, 1));
        var handler = new StubHttpHandler(_ => JsonResponse(InfoJson(HubId.Value)));
        var transport = new WifiInfraredHubTransport(
            store,
            new WifiInfraredHubApiClient(new HttpClient(handler)),
            TransmitClient());

        var info = await transport.GetInfoAsync(HubId);
        var learn = await transport.LearnAsync(HubId);

        Assert.Equal(InfraredHubConnectionState.Ready, info.State);
        Assert.Equal("1.2.3", info.FirmwareVersion);
        Assert.Equal(InfraredHubLearnOutcome.Unsupported, learn.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Redirect_is_never_followed_as_a_hub_identity_probe()
    {
        var connection = new WifiInfraredHubConnection(HubId, "192.168.1.44", 8081, 1);
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("http://8.8.8.8/evil");
            return response;
        });
        var api = new WifiInfraredHubApiClient(new HttpClient(handler));

        var result = await api.ProbeAsync(connection);

        Assert.Equal(WifiInfraredHubProbeOutcome.InvalidResponse, result.Outcome);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Missing_candidate_and_saved_connection_returns_not_found()
    {
        var store = new InMemoryWifiInfraredHubConnectionStore();
        var api = Api(InfoJson(HubId.Value));
        var connector = new WifiInfraredHubConnector(new WifiInfraredHubCandidateCache(), store, api, new WifiInfraredHubTransport(store, api, TransmitClient()));

        var result = await connector.ConnectAsync(HubId);

        Assert.Equal(InfraredHubConnectOutcome.NotFound, result.Outcome);
        Assert.Null(result.Hub);
    }

    [Fact]
    public void Dependency_injection_exposes_transport_neutral_discovery_and_connector()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IDeviceDiscovery>(new FakeDeviceDiscovery([]));
        services.AddUniversalRemoteInfraredHubWifi();
        using var provider = services.BuildServiceProvider();

        Assert.Contains(provider.GetServices<IInfraredHubDiscovery>(), x => x.TransportKind == InfraredHubTransportKind.Wifi);
        Assert.Contains(provider.GetServices<IInfraredHubConnector>(), x => x.TransportKind == InfraredHubTransportKind.Wifi);
        Assert.Contains(provider.GetServices<IInfraredHubTransport>(), x => x.Kind == InfraredHubTransportKind.Wifi);
    }

    private static WifiInfraredHubApiClient Api(string json)
        => new(new HttpClient(new StubHttpHandler(_ => JsonResponse(json))));

    private static WifiInfraredHubTransmitClient TransmitClient()
        => new(new HttpClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))));

    private static HttpResponseMessage JsonResponse(string json)
        => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string InfoJson(string hubId, int schemaVersion = 1)
        => $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "hubId": "{{hubId}}",
          "displayName": "Hub IR Salon",
          "firmwareVersion": "1.2.3",
          "state": "ready",
          "capabilities": {
            "canTransmit": true,
            "canLearn": true,
            "carrierFrequencies": [{ "minHz": 36000, "maxHz": 40000 }],
            "maxPatternValues": 4096,
            "maxTotalDurationMicroseconds": 2000000
          }
        }
        """;

    private static DiscoveredDevice HubDevice(string address)
        => new()
        {
            DiscoveryId = Guid.NewGuid().ToString("N"),
            DisplayName = "UniversalRemote IR",
            HostName = "universalremote-ir.local",
            Addresses = new HashSet<string>([address], StringComparer.OrdinalIgnoreCase),
            Services = new HashSet<string>([WifiInfraredHubProtocol.MdnsServiceType], StringComparer.OrdinalIgnoreCase),
            Sources = new HashSet<string>(["mdns"], StringComparer.OrdinalIgnoreCase),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["port"] = "8081",
                ["txt"] = $"id={HubId.Value};api=1;name=Salon IR;fw=1.2.3"
            }
        };

    private sealed class FakeDeviceDiscovery(IReadOnlyList<DiscoveredDevice> devices) : IDeviceDiscovery
    {
        public Task<IReadOnlyList<DiscoveredDevice>> DiscoverAsync(DiscoveryScanOptions? options = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(devices);
        }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(responder(request));
        }
    }
}
