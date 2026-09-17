using System.Buffers;
using System.Net;
using System.Text;

namespace UniversalRemote.Media.Epg;

public sealed class XmlTvClient
{
    private const int MaxDocumentBytes = 24 * 1024 * 1024;
    private readonly HttpClient httpClient;

    public XmlTvClient(HttpClient httpClient) => this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<string> DownloadAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new EpgSourceConfigurationException("The EPG endpoint must be an absolute HTTP(S) URI.");

        try
        {
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new EpgLoadException("The EPG server returned an unsuccessful response.", response.StatusCode);

            if (response.Content.Headers.ContentLength is > MaxDocumentBytes)
                throw new EpgLoadException("The EPG document is larger than the supported limit.");

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var output = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                var total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    total += read;
                    if (total > MaxDocumentBytes)
                        throw new EpgLoadException("The EPG document is larger than the supported limit.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (OperationCanceledException) { throw; }
        catch (EpgLoadException) { throw; }
        catch (HttpRequestException)
        {
            throw new EpgLoadException("The EPG server could not be reached.");
        }
    }
}
