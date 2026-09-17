using System.Buffers;
using System.Text;

namespace UniversalRemote.Media.Provider.M3U;

/// <summary>Downloads a playlist with an explicit size limit and redacted failures.</summary>
public sealed class M3uPlaylistClient
{
    public const int DefaultMaximumPlaylistBytes = 8 * 1024 * 1024;

    private readonly HttpClient httpClient;
    private readonly int maximumPlaylistBytes;

    public M3uPlaylistClient(HttpClient httpClient)
        : this(httpClient, DefaultMaximumPlaylistBytes)
    {
    }

    public M3uPlaylistClient(HttpClient httpClient, int maximumPlaylistBytes)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (maximumPlaylistBytes < 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumPlaylistBytes), "Playlist limit must be at least 1 KiB.");
        this.maximumPlaylistBytes = maximumPlaylistBytes;
    }

    public async Task<string> DownloadAsync(Uri playlistUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(playlistUri);
        if (!playlistUri.IsAbsoluteUri || !IsHttp(playlistUri))
            throw new M3uSourceConfigurationException("M3U playlist URI must use HTTP or HTTPS.");

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, playlistUri);
            response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new M3uPlaylistLoadException("The M3U playlist request timed out.");
        }
        catch (HttpRequestException)
        {
            throw new M3uPlaylistLoadException("The M3U playlist could not be loaded.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new M3uPlaylistLoadException("The M3U playlist server returned an error.", response.StatusCode);

            if (response.Content.Headers.ContentLength is long declaredLength && declaredLength > maximumPlaylistBytes)
                throw new M3uPlaylistLoadException("The M3U playlist exceeds the configured size limit.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var bytes = await ReadLimitedAsync(stream, maximumPlaylistBytes, cancellationToken).ConfigureAwait(false);
            return DecodeText(bytes);
        }
    }

    private static bool IsHttp(Uri uri)
        => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
           uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            using var output = new MemoryStream(Math.Min(maximumBytes, 256 * 1024));
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > maximumBytes)
                    throw new M3uPlaylistLoadException("The M3U playlist exceeds the configured size limit.");
                output.Write(buffer, 0, read);
            }
            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }
}
