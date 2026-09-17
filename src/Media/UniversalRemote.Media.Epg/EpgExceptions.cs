namespace UniversalRemote.Media.Epg;

public sealed class EpgSourceConfigurationException : InvalidOperationException
{
    public EpgSourceConfigurationException(string message) : base(message) { }
}

public sealed class EpgLoadException : InvalidOperationException
{
    public System.Net.HttpStatusCode? StatusCode { get; }

    public EpgLoadException(string message, System.Net.HttpStatusCode? statusCode = null) : base(message)
        => StatusCode = statusCode;
}

public sealed class EpgFormatException : FormatException
{
    public EpgFormatException(string message) : base(message) { }
}
