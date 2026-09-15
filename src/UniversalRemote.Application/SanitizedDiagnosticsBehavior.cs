using System.Diagnostics;
using DomainRelay.Abstractions;
using DomainRelay.Diagnostics;

namespace UniversalRemote.Application;

/// <summary>Records operation types and outcomes without exception messages or request payloads.</summary>
public sealed class SanitizedDiagnosticsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, HandlerDelegate<TResponse> next, CancellationToken ct)
    {
        using var activity = DomainRelayActivity.Source.StartActivity($"DomainRelay.Send {typeof(TRequest).Name}", ActivityKind.Internal);
        activity?.SetTag("domainrelay.request", typeof(TRequest).FullName);
        activity?.SetTag("domainrelay.response", typeof(TResponse).FullName);
        try
        {
            var response = await next().ConfigureAwait(false);
            activity?.SetTag("domainrelay.success", true);
            return response;
        }
        catch (Exception exception)
        {
            activity?.SetTag("domainrelay.success", false);
            activity?.SetTag("domainrelay.exception.type", exception.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }
}
