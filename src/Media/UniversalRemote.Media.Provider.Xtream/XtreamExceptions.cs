using System.Net;

namespace UniversalRemote.Media.Provider.Xtream;

public class XtreamException : InvalidOperationException
{
    public XtreamException(string message) : base(message) { }
}

public sealed class XtreamSourceConfigurationException : XtreamException
{
    public XtreamSourceConfigurationException(string message) : base(message) { }
}

public sealed class XtreamAuthenticationException : XtreamException
{
    public XtreamAuthenticationException(string message) : base(message) { }
}

public sealed class XtreamApiException : XtreamException
{
    public HttpStatusCode? StatusCode { get; }

    public XtreamApiException(string message, HttpStatusCode? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
