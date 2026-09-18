using System.Buffers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace UniversalRemote.Remote.Provider.AndroidTv;

internal static class AndroidTvProtocol
{
    public const int PairingPort = 6467;
    public const int RemotePort = 6466;
    public const string ServiceType = "_androidtvremote2._tcp";

    public static byte[] PairingRequest(string serviceName, string clientName)
        => Outer(10, Message(w => { w.String(1, serviceName); w.String(2, clientName); }));

    public static byte[] PairingOptions()
    {
        var encoding = Message(w => { w.Varint(1, 3); w.Varint(2, 6); });
        return Outer(20, Message(w => { w.Bytes(1, encoding); w.Varint(2, 1); }));
    }

    public static byte[] PairingConfiguration()
    {
        var encoding = Message(w => { w.Varint(1, 3); w.Varint(2, 6); });
        return Outer(30, Message(w => { w.Bytes(1, encoding); w.Varint(2, 1); }));
    }

    public static byte[] PairingSecret(ReadOnlySpan<byte> secret)
    {
        using var stream = new MemoryStream();
        new ProtoWriter(stream).Bytes(1, secret);
        return Outer(40, stream.ToArray());
    }

    public static byte[] RemoteConfigure()
    {
        var info = Message(w =>
        {
            w.String(1, "UniversalRemote");
            w.String(2, "OpenAI/.NET");
            w.Varint(3, 1);
            w.String(4, "UniversalRemote");
            w.String(5, "UniversalRemote");
            w.String(6, "0.2.0-preview.1");
        });
        var configure = Message(w => { w.Varint(1, 622); w.Bytes(2, info); });
        return Message(w => w.Bytes(1, configure));
    }

    public static byte[] RemoteKeyInject(int keyCode)
    {
        var key = Message(w => { w.Varint(1, (ulong)keyCode); w.Varint(2, 3); });
        return Message(w => w.Bytes(10, key));
    }

    public static byte[] RemotePingResponse(ulong value)
    {
        var ping = Message(w => w.Varint(1, value));
        return Message(w => w.Bytes(9, ping));
    }

    public static bool TryReadPing(ReadOnlySpan<byte> message, out ulong value)
    {
        value = 0;
        if (!ProtoReader.TryGetLengthDelimited(message, 8, out var ping)) return false;
        return ProtoReader.TryGetVarint(ping, 1, out value);
    }

    public static bool HasField(ReadOnlySpan<byte> message, int fieldNumber)
        => ProtoReader.HasField(message, fieldNumber);

    public static byte[] ComputePairingSecret(X509Certificate2 client, X509Certificate2 server, string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != 6 || normalized.Any(c => !Uri.IsHexDigit(c)))
            throw new ArgumentException("Android TV pairing code must contain exactly six hexadecimal characters.", nameof(code));

        using var clientRsa = client.GetRSAPublicKey() ?? throw new CryptographicException("Client certificate is not RSA.");
        using var serverRsa = server.GetRSAPublicKey() ?? throw new CryptographicException("Server certificate is not RSA.");
        var clientKey = clientRsa.ExportParameters(false);
        var serverKey = serverRsa.ExportParameters(false);
        var suffix = Convert.FromHexString(normalized[2..]);

        using var buffer = new MemoryStream();
        WriteRequired(buffer, clientKey.Modulus);
        buffer.WriteByte(0);
        WriteRequired(buffer, clientKey.Exponent);
        WriteRequired(buffer, serverKey.Modulus);
        buffer.WriteByte(0);
        WriteRequired(buffer, serverKey.Exponent);
        buffer.Write(suffix);
        var hash = SHA256.HashData(buffer.ToArray());
        if (hash[0] != Convert.ToByte(normalized[..2], 16))
            throw new CryptographicException("Pairing code validation failed.");
        return hash;
    }

    public static async Task WriteFrameAsync(Stream stream, ReadOnlyMemory<byte> payload, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Span<byte> prefix = stackalloc byte[10];
            var length = WriteVarint(prefix, (ulong)payload.Length);
            await stream.WriteAsync(prefix[..length].ToArray(), ct).ConfigureAwait(false);
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }

    public static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken ct)
    {
        var length = await ReadVarintAsync(stream, ct).ConfigureAwait(false);
        if (length > 1024 * 1024) throw new InvalidDataException("Android TV protocol frame is too large.");
        var buffer = new byte[checked((int)length)];
        await stream.ReadExactlyAsync(buffer, ct).ConfigureAwait(false);
        return buffer;
    }

    public static X509Certificate2 CreateClientCertificate(out string password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=UniversalRemote", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(3));
        password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var pfx = certificate.Export(X509ContentType.Pkcs12, password);
        return X509CertificateLoader.LoadPkcs12(pfx, password, X509KeyStorageFlags.Exportable);
    }

    private static byte[] Outer(int fieldNumber, byte[] body)
        => Message(w => { w.Varint(1, 2); w.Varint(2, 200); w.Bytes(fieldNumber, body); });

    private static byte[] Message(Action<ProtoWriter> build)
    {
        using var stream = new MemoryStream();
        build(new ProtoWriter(stream));
        return stream.ToArray();
    }

    private static void WriteRequired(Stream stream, byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) throw new CryptographicException("Missing RSA public-key parameter.");
        stream.Write(bytes);
    }

    private static int WriteVarint(Span<byte> target, ulong value)
    {
        var i = 0;
        do
        {
            var b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            target[i++] = b;
        } while (value != 0);
        return i;
    }

    private static async Task<ulong> ReadVarintAsync(Stream stream, CancellationToken ct)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            var one = new byte[1];
            await stream.ReadExactlyAsync(one, ct).ConfigureAwait(false);
            var b = one[0];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw new InvalidDataException("Invalid protobuf varint.");
    }

    internal sealed class ProtoWriter(Stream stream)
    {
        public void Varint(int field, ulong value)
        {
            Key(field, 0);
            Span<byte> bytes = stackalloc byte[10];
            var length = WriteVarint(bytes, value);
            stream.Write(bytes[..length]);
        }
        public void String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));
        public void Bytes(int field, ReadOnlySpan<byte> value)
        {
            Key(field, 2);
            Span<byte> length = stackalloc byte[10];
            var count = WriteVarint(length, (ulong)value.Length);
            stream.Write(length[..count]);
            stream.Write(value);
        }
        private void Key(int field, int wireType) => VarintRaw((ulong)((field << 3) | wireType));
        private void VarintRaw(ulong value)
        {
            Span<byte> bytes = stackalloc byte[10];
            var length = WriteVarint(bytes, value);
            stream.Write(bytes[..length]);
        }
    }

    private static class ProtoReader
    {
        public static bool HasField(ReadOnlySpan<byte> source, int expected)
        {
            var offset = 0;
            while (TryField(source, ref offset, out var field, out _, out _)) if (field == expected) return true;
            return false;
        }

        public static bool TryGetLengthDelimited(ReadOnlySpan<byte> source, int expected, out ReadOnlySpan<byte> value)
        {
            var offset = 0;
            while (TryField(source, ref offset, out var field, out var wire, out var data))
                if (field == expected && wire == 2) { value = data; return true; }
            value = default;
            return false;
        }

        public static bool TryGetVarint(ReadOnlySpan<byte> source, int expected, out ulong value)
        {
            var offset = 0;
            while (offset < source.Length)
            {
                if (!ReadVarint(source, ref offset, out var key)) break;
                var field = (int)(key >> 3);
                var wire = (int)(key & 7);
                if (wire == 0)
                {
                    if (!ReadVarint(source, ref offset, out var item)) break;
                    if (field == expected) { value = item; return true; }
                }
                else if (!Skip(source, ref offset, wire)) break;
            }
            value = 0;
            return false;
        }

        private static bool TryField(ReadOnlySpan<byte> source, scoped ref int offset, out int field, out int wire, out ReadOnlySpan<byte> data)
        {
            field = 0; wire = 0; data = default;
            if (offset >= source.Length || !ReadVarint(source, ref offset, out var key)) return false;
            field = (int)(key >> 3); wire = (int)(key & 7);
            if (wire == 2)
            {
                if (!ReadVarint(source, ref offset, out var length) || length > (ulong)(source.Length - offset)) return false;
                data = source.Slice(offset, (int)length); offset += (int)length; return true;
            }
            if (wire == 0) return ReadVarint(source, ref offset, out _);
            return Skip(source, ref offset, wire);
        }

        private static bool Skip(ReadOnlySpan<byte> source, ref int offset, int wire)
        {
            switch (wire)
            {
                case 0: return ReadVarint(source, ref offset, out _);
                case 1: if (offset + 8 > source.Length) return false; offset += 8; return true;
                case 2:
                    if (!ReadVarint(source, ref offset, out var len) || len > (ulong)(source.Length - offset)) return false;
                    offset += (int)len; return true;
                case 5: if (offset + 4 > source.Length) return false; offset += 4; return true;
                default: return false;
            }
        }

        private static bool ReadVarint(ReadOnlySpan<byte> source, ref int offset, out ulong value)
        {
            value = 0;
            for (var shift = 0; shift < 64 && offset < source.Length; shift += 7)
            {
                var b = source[offset++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
            }
            return false;
        }
    }
}
