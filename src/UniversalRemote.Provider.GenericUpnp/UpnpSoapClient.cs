using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace UniversalRemote.Remote.Provider.GenericUpnp;

internal sealed class UpnpSoapClient(HttpClient httpClient)
{
    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private readonly HttpClient http = httpClient;

    public async Task<XDocument> InvokeAsync(Uri controlUri, string serviceType, string action, IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var body = new XElement(XName.Get(action, serviceType), args.Select(x => new XElement(x.Key, x.Value)));
        var envelope = new XDocument(new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "s", Soap.NamespaceName),
            new XAttribute(Soap + "encodingStyle", "http://schemas.xmlsoap.org/soap/encoding/"),
            new XElement(Soap + "Body", body)));
        using var request = new HttpRequestMessage(HttpMethod.Post, controlUri);
        request.Content = new StringContent(envelope.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml");
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{serviceType}#{action}\"");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        return await XDocument.LoadAsync(stream, LoadOptions.None, ct).ConfigureAwait(false);
    }

    public static string Value(XDocument document, string localName)
        => document.Descendants().FirstOrDefault(x => x.Name.LocalName == localName)?.Value
           ?? throw new InvalidDataException($"UPnP response does not contain {localName}.");
}
