using UniversalRemote.Media.Abstractions;

namespace UniversalRemote.Media.Provider.Xtream;

public sealed class XtreamSourceSetupProvider : IMediaSourceSetupProvider
{
    private static readonly IReadOnlyList<MediaSourceSetupField> SetupFields =
    [
        new("server", "Serveur", "http://serveur:port/", MediaSourceSetupFieldKind.Uri),
        new("username", "Utilisateur", "Utilisateur", MediaSourceSetupFieldKind.Text),
        new("password", "Mot de passe", "Mot de passe", MediaSourceSetupFieldKind.Secret)
    ];

    public string ProviderId => XtreamMediaProvider.ProviderId;
    public string DisplayName => "Xtream-compatible";
    public IReadOnlyList<MediaSourceSetupField> Fields => SetupFields;

    public MediaSecret CreateSecret(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (!values.TryGetValue("server", out var rawServer) || !Uri.TryCreate(rawServer?.Trim(), UriKind.Absolute, out var server))
            throw new XtreamSourceConfigurationException("Xtream server URI is invalid.");
        if (!values.TryGetValue("username", out var username) || string.IsNullOrWhiteSpace(username))
            throw new XtreamSourceConfigurationException("Xtream username is required.");
        if (!values.TryGetValue("password", out var password) || string.IsNullOrWhiteSpace(password))
            throw new XtreamSourceConfigurationException("Xtream password is required.");

        try { return XtreamCredentialCodec.Encode(new XtreamCredentials(server, username, password)); }
        catch (ArgumentException) { throw new XtreamSourceConfigurationException("Xtream server URI is invalid."); }
    }
}
