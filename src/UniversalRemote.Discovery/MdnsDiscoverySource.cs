using System.Collections.Frozen;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UniversalRemote.Remote.Discovery;

public sealed class MdnsDiscoverySource : IDiscoverySource
{
    private static readonly IPEndPoint MulticastEndpoint = new(IPAddress.Parse("224.0.0.251"), 5353);
    public string Id => "mdns";

    public async Task<IReadOnlyList<DiscoveryCandidate>> DiscoverAsync(DiscoveryScanOptions options, CancellationToken cancellationToken)
    {
        options.Validate();
        using var client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
        client.JoinMulticastGroup(MulticastEndpoint.Address);

        foreach (var serviceType in options.MdnsServiceTypes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(serviceType)) continue;
            var packet = BuildPtrQuery(serviceType);
            await client.SendAsync(packet.AsMemory(), MulticastEndpoint, cancellationToken).ConfigureAwait(false);
        }

        var records = new List<DnsRecord>();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var response = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                records.AddRange(ParseRecords(response.Buffer));
            }
            catch (OperationCanceledException) { break; }
            catch (InvalidDataException) { }
        }
        return BuildCandidates(records, options.MdnsServiceTypes);
    }

    internal static byte[] BuildPtrQuery(string serviceType)
    {
        var bytes = new List<byte>(128) { 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
        foreach (var label in serviceType.TrimEnd('.').Split('.'))
        {
            var labelBytes = Encoding.UTF8.GetBytes(label);
            if (labelBytes.Length is 0 or > 63) throw new ArgumentException("Invalid mDNS service type.", nameof(serviceType));
            bytes.Add((byte)labelBytes.Length);
            bytes.AddRange(labelBytes);
        }
        bytes.Add(0);
        bytes.AddRange([0, 12, 0, 1]); // PTR / IN
        return bytes.ToArray();
    }

    internal static IReadOnlyList<DnsRecord> ParseRecords(byte[] packet)
    {
        if (packet.Length < 12) throw new InvalidDataException("Invalid DNS packet.");
        var questionCount = ReadUInt16(packet, 4);
        var answerCount = ReadUInt16(packet, 6);
        var authorityCount = ReadUInt16(packet, 8);
        var additionalCount = ReadUInt16(packet, 10);
        var offset = 12;
        for (var i = 0; i < questionCount; i++)
        {
            _ = ReadName(packet, ref offset);
            Ensure(packet, offset, 4);
            offset += 4;
        }

        var total = answerCount + authorityCount + additionalCount;
        var records = new List<DnsRecord>(total);
        for (var i = 0; i < total; i++)
        {
            var name = ReadName(packet, ref offset);
            Ensure(packet, offset, 10);
            var type = ReadUInt16(packet, offset); offset += 2;
            offset += 2; // class
            offset += 4; // ttl
            var length = ReadUInt16(packet, offset); offset += 2;
            Ensure(packet, offset, length);
            var dataOffset = offset;
            string? target = null;
            string? text = null;
            string? address = null;
            ushort? port = null;
            if (type == 12) // PTR
            {
                var cursor = dataOffset;
                target = ReadName(packet, ref cursor);
            }
            else if (type == 33 && length >= 6) // SRV
            {
                port = ReadUInt16(packet, dataOffset + 4);
                var cursor = dataOffset + 6;
                target = ReadName(packet, ref cursor);
            }
            else if (type == 1 && length == 4)
            {
                address = new IPAddress(packet.AsSpan(dataOffset, 4)).ToString();
            }
            else if (type == 28 && length == 16)
            {
                address = new IPAddress(packet.AsSpan(dataOffset, 16)).ToString();
            }
            else if (type == 16)
            {
                text = ParseTxt(packet.AsSpan(dataOffset, length));
            }
            records.Add(new DnsRecord(name, type, target, address, port, text));
            offset += length;
        }
        return records;
    }

    internal static IReadOnlyList<DiscoveryCandidate> BuildCandidates(
        IEnumerable<DnsRecord> source,
        IEnumerable<string>? requestedServiceTypes = null)
    {
        var records = source.ToArray();
        var requested = (requestedServiceTypes ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizeDnsName)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        var ptrs = records
            .Where(x => x.Type == 12 && !string.IsNullOrWhiteSpace(x.Target))
            .Where(x => requested.Count == 0 || requested.Contains(NormalizeDnsName(x.Name)))
            .ToArray();
        var results = new List<DiscoveryCandidate>();
        foreach (var ptr in ptrs)
        {
            var instance = ptr.Target!;
            var srv = records.FirstOrDefault(x => x.Type == 33 && string.Equals(x.Name, instance, StringComparison.OrdinalIgnoreCase));
            var host = srv?.Target;
            var addresses = string.IsNullOrWhiteSpace(host)
                ? Array.Empty<string>()
                : records.Where(x => (x.Type is 1 or 28) && string.Equals(x.Name, host, StringComparison.OrdinalIgnoreCase) && x.Address is not null)
                    .Select(x => x.Address!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var txt = records.FirstOrDefault(x => x.Type == 16 && string.Equals(x.Name, instance, StringComparison.OrdinalIgnoreCase))?.Text;
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (srv?.Port is not null) metadata["port"] = srv.Port.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(txt)) metadata["txt"] = txt;
            results.Add(new DiscoveryCandidate
            {
                SourceId = "mdns",
                StableKey = instance,
                DisplayName = InstanceDisplayName(instance, ptr.Name),
                HostName = host?.TrimEnd('.'),
                Addresses = addresses.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                Services = new[] { ptr.Name.TrimEnd('.') }.ToFrozenSet(StringComparer.OrdinalIgnoreCase),
                Metadata = metadata.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
            });
        }
        return results;
    }

    internal sealed record DnsRecord(string Name, ushort Type, string? Target, string? Address, ushort? Port, string? Text);

    private static string NormalizeDnsName(string value) => value.Trim().TrimEnd('.');

    private static string InstanceDisplayName(string instance, string service)
    {
        var normalized = instance.TrimEnd('.');
        var suffix = "." + service.TrimEnd('.');
        return normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normalized[..^suffix.Length]
            : normalized;
    }

    private static string ParseTxt(ReadOnlySpan<byte> data)
    {
        var items = new List<string>();
        var offset = 0;
        while (offset < data.Length)
        {
            var length = data[offset++];
            if (offset + length > data.Length) break;
            items.Add(Encoding.UTF8.GetString(data.Slice(offset, length)));
            offset += length;
        }
        return string.Join(';', items);
    }

    private static string ReadName(byte[] packet, ref int offset)
    {
        var labels = new List<string>();
        var cursor = offset;
        var jumped = false;
        var visited = new HashSet<int>();
        for (var guard = 0; guard < 128; guard++)
        {
            Ensure(packet, cursor, 1);
            var length = packet[cursor++];
            if (length == 0)
            {
                if (!jumped) offset = cursor;
                return string.Join('.', labels) + (labels.Count > 0 ? "." : string.Empty);
            }
            if ((length & 0xC0) == 0xC0)
            {
                Ensure(packet, cursor, 1);
                var pointer = ((length & 0x3F) << 8) | packet[cursor++];
                if (!jumped) offset = cursor;
                if (!visited.Add(pointer)) throw new InvalidDataException("DNS compression loop.");
                cursor = pointer;
                jumped = true;
                continue;
            }
            if ((length & 0xC0) != 0 || length > 63) throw new InvalidDataException("Invalid DNS label.");
            Ensure(packet, cursor, length);
            labels.Add(Encoding.UTF8.GetString(packet, cursor, length));
            cursor += length;
            if (!jumped) offset = cursor;
        }
        throw new InvalidDataException("DNS name too deep.");
    }

    private static ushort ReadUInt16(byte[] packet, int offset)
    {
        Ensure(packet, offset, 2);
        return (ushort)((packet[offset] << 8) | packet[offset + 1]);
    }

    private static void Ensure(byte[] packet, int offset, int count)
    {
        if (offset < 0 || count < 0 || offset > packet.Length - count) throw new InvalidDataException("Truncated DNS packet.");
    }
}
