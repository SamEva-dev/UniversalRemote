using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using UniversalRemote.Remote.Discovery;
using Xunit;

namespace UniversalRemote.Remote.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public void Consolidator_merges_candidates_that_share_an_address()
    {
        var consolidator = new DiscoveryConsolidator();
        var first = Candidate("mdns", "living-room._googlecast._tcp.local", "Living room", "tv.local", ["192.168.1.20"], ["_googlecast._tcp.local"]);
        var second = Candidate("ssdp", "uuid:tv-1", "TV server", null, ["192.168.1.20"], ["urn:schemas-upnp-org:device:MediaRenderer:1"]);

        var result = Assert.Single(consolidator.Consolidate([first, second]));

        Assert.Equal("Living room", result.DisplayName);
        Assert.Contains("mdns", result.Sources);
        Assert.Contains("ssdp", result.Sources);
        Assert.Contains("_googlecast._tcp.local", result.Services);
        Assert.Contains("urn:schemas-upnp-org:device:MediaRenderer:1", result.Services);
    }

    [Fact]
    public async Task DeviceDiscovery_combines_all_registered_sources()
    {
        IDiscoverySource[] sources =
        [
            new FakeSource("one", [Candidate("one", "a", "A", "a.local", ["10.0.0.10"], ["svc-a"])]),
            new FakeSource("two", [Candidate("two", "b", "B", "b.local", ["10.0.0.11"], ["svc-b"])])
        ];
        var discovery = new DeviceDiscovery(sources, new DiscoveryConsolidator(), new NoopDiscoveryNetworkLease());

        var result = await discovery.DiscoverAsync(new DiscoveryScanOptions { Timeout = TimeSpan.FromSeconds(1) });

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Ssdp_parser_is_case_insensitive_and_keeps_location()
    {
        const string payload = "HTTP/1.1 200 OK\r\nLOCATION: http://192.168.1.20:8008/ssdp/device-desc.xml\r\nUsn: uuid:test::upnp:rootdevice\r\nST: upnp:rootdevice\r\nSERVER: Test/1.0 UPnP/1.1\r\n\r\n";

        var headers = SsdpDiscoverySource.ParseHeaders(payload);

        Assert.Equal("uuid:test::upnp:rootdevice", headers["USN"]);
        Assert.StartsWith("http://192.168.1.20", headers["LOCATION"]);
    }

    [Fact]
    public void Mdns_query_contains_ptr_question_for_service_type()
    {
        var packet = MdnsDiscoverySource.BuildPtrQuery("_googlecast._tcp.local");

        Assert.True(packet.Length > 20);
        Assert.Equal(1, packet[5]); // QDCOUNT = 1
        Assert.Equal(12, packet[^3]); // QTYPE PTR low byte
        Assert.Equal(1, packet[^1]); // QCLASS IN low byte
    }

    [Fact]
    public void Mdns_candidates_ignore_unsolicited_ptr_records_outside_requested_services()
    {
        var records = new[]
        {
            new MdnsDiscoverySource.DnsRecord("_androidtvremote2._tcp.local.", 12, "TCL._androidtvremote2._tcp.local.", null, null, null),
            new MdnsDiscoverySource.DnsRecord("TCL._androidtvremote2._tcp.local.", 33, "tcl.local.", null, 6467, null),
            new MdnsDiscoverySource.DnsRecord("tcl.local.", 1, null, "192.168.1.86", null, null),
            new MdnsDiscoverySource.DnsRecord("_services._dns-sd._udp.local.", 12, "_nearby-presence._tcp.local.", null, null, null)
        };

        var result = MdnsDiscoverySource.BuildCandidates(records, ["_androidtvremote2._tcp.local"]);

        var device = Assert.Single(result);
        Assert.Equal("TCL", device.DisplayName);
        Assert.Contains("192.168.1.86", device.Addresses);
        Assert.Contains("_androidtvremote2._tcp.local", device.Services);
    }

    [Fact]
    public void Discovery_registration_exposes_orchestrator_and_both_sources()
    {
        var services = new ServiceCollection();
        services.AddUniversalRemoteDiscovery();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDeviceDiscovery>());
        var sources = provider.GetServices<IDiscoverySource>().Select(x => x.Id).OrderBy(x => x).ToArray();
        Assert.Equal(["mdns", "ssdp"], sources);
    }

    private static DiscoveryCandidate Candidate(string source, string key, string name, string? host, string[] addresses, string[] services)
        => new()
        {
            SourceId = source,
            StableKey = key,
            DisplayName = name,
            HostName = host,
            Addresses = addresses.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
            Services = services.ToFrozenSet(StringComparer.OrdinalIgnoreCase)
        };

    private sealed class FakeSource(string id, IReadOnlyList<DiscoveryCandidate> results) : IDiscoverySource
    {
        public string Id => id;
        public Task<IReadOnlyList<DiscoveryCandidate>> DiscoverAsync(DiscoveryScanOptions options, CancellationToken cancellationToken)
            => Task.FromResult(results);
    }
}
