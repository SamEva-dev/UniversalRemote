using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Epg;

public sealed class XmlTvEpgProvider : IEpgProvider
{
    public const string ProviderId = "xmltv";

    private readonly IMediaCredentialStore credentialStore;
    private readonly XmlTvClient client;
    private readonly XmlTvParser parser;

    public string Id => ProviderId;

    public XmlTvEpgProvider(IMediaCredentialStore credentialStore, XmlTvClient client, XmlTvParser parser)
    {
        this.credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<EpgDocumentSnapshot> GetAsync(EpgSource source, EpgGuideRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        if (!source.IsEnabled) throw new EpgSourceConfigurationException("The EPG source is disabled.");
        if (!string.Equals(source.ProviderId, ProviderId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("EPG source does not belong to the XMLTV provider.", nameof(source));

        var secret = await credentialStore.GetAsync(source.CredentialReference, cancellationToken).ConfigureAwait(false)
                     ?? throw new EpgSourceConfigurationException("The EPG source credentials are unavailable.");
        var endpoint = ParseEndpoint(secret);
        var xml = await client.DownloadAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return parser.Parse(xml, request.WindowStart, request.WindowEnd);
    }

    private static Uri ParseEndpoint(MediaSecret secret)
    {
        var raw = secret.Reveal().Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var endpoint) ||
            !(endpoint.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) || endpoint.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new EpgSourceConfigurationException("The EPG source credentials do not contain a valid HTTP(S) XMLTV endpoint.");
        return endpoint;
    }
}
