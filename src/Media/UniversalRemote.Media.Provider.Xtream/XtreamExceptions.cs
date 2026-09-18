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

public enum XtreamApiFailureKind
{
    Unknown,
    Network,
    Timeout,
    Redirect,
    HttpStatus,
    InvalidJson,
    InvalidPayload,
    ResponseTooLarge
}

public sealed class XtreamApiException : XtreamException
{
    public HttpStatusCode? StatusCode { get; }
    public XtreamApiFailureKind FailureKind { get; }

    public XtreamApiException(
        string message,
        HttpStatusCode? statusCode = null,
        XtreamApiFailureKind failureKind = XtreamApiFailureKind.Unknown)
        : base(message)
    {
        StatusCode = statusCode;
        FailureKind = failureKind;
    }
}
