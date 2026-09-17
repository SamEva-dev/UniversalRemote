using System.Text;
using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.Xtream;

/// <summary>
/// Credentials for an Xtream-compatible Player API. The object deliberately redacts its string representation.
/// Persist it only through <see cref="IMediaCredentialStore"/>.
/// </summary>
public sealed class XtreamCredentials
{
    public Uri ServerBaseUri { get; }
    public string Username { get; }
    public string Password { get; }

    public XtreamCredentials(Uri serverBaseUri, string username, string password)
    {
        ArgumentNullException.ThrowIfNull(serverBaseUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        if (!serverBaseUri.IsAbsoluteUri || !IsHttp(serverBaseUri))
            throw new ArgumentException("Xtream server must use an absolute HTTP or HTTPS URI.", nameof(serverBaseUri));
        if (!string.IsNullOrEmpty(serverBaseUri.UserInfo) || !string.IsNullOrEmpty(serverBaseUri.Query) || !string.IsNullOrEmpty(serverBaseUri.Fragment))
            throw new ArgumentException("Xtream server URI must not contain user info, query or fragment data.", nameof(serverBaseUri));

        var normalized = new UriBuilder(serverBaseUri)
        {
            Path = EnsureTrailingSlash(serverBaseUri.AbsolutePath),
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;

        ServerBaseUri = normalized;
        Username = username.Trim();
        Password = password;
    }

    public bool UsesTls => ServerBaseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => $"XtreamCredentials(Server={ServerBaseUri.GetLeftPart(UriPartial.Authority)}, Username=[REDACTED], Password=[REDACTED])";

    private static bool IsHttp(Uri uri)
        => uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
           || uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string EnsureTrailingSlash(string path)
    {
        if (string.IsNullOrEmpty(path)) return "/";
        return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
    }
}

/// <summary>
/// AOT-friendly opaque codec used to persist Xtream credentials as one MediaSecret.
/// Base64 is only an encoding; confidentiality is supplied by the platform secure store.
/// </summary>
public static class XtreamCredentialCodec
{
    private const string Version = "UR-XTREAM-1";

    public static MediaSecret Encode(XtreamCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var payload = string.Join('\n',
            Version,
            EncodePart(credentials.ServerBaseUri.AbsoluteUri),
            EncodePart(credentials.Username),
            EncodePart(credentials.Password));
        return new MediaSecret(payload);
    }

    public static XtreamCredentials Decode(MediaSecret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        var parts = secret.Reveal().Split('\n');
        if (parts.Length != 4 || !string.Equals(parts[0], Version, StringComparison.Ordinal))
            throw new XtreamSourceConfigurationException("Stored Xtream credentials use an unsupported format.");

        try
        {
            var server = new Uri(DecodePart(parts[1]), UriKind.Absolute);
            return new XtreamCredentials(server, DecodePart(parts[2]), DecodePart(parts[3]));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new XtreamSourceConfigurationException("Stored Xtream credentials are invalid.");
        }
    }

    private static string EncodePart(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    private static string DecodePart(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
}
