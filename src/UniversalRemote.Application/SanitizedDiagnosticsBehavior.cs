using System.Diagnostics;
using DomainRelay.Abstractions;
using DomainRelay.Diagnostics;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Application;

/// <summary>Records operation types and outcomes without exception messages or request payloads.</summary>
public sealed class SanitizedDiagnosticsBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly ITelemetryRecorder telemetry;

    public SanitizedDiagnosticsBehavior(ITelemetryRecorder? telemetry = null)
        => this.telemetry = telemetry ?? NullTelemetryRecorder.Instance;

    public async Task<TResponse> Handle(TRequest request, HandlerDelegate<TResponse> next, CancellationToken ct)
    {
        using var activity = DomainRelayActivity.Source.StartActivity($"DomainRelay.Send {typeof(TRequest).Name}", ActivityKind.Internal);
        activity?.SetTag("domainrelay.request", typeof(TRequest).FullName);
        activity?.SetTag("domainrelay.response", typeof(TResponse).FullName);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next().ConfigureAwait(false);
            var succeeded = response is not RemoteResult result || result.IsSuccess;
            activity?.SetTag("domainrelay.success", succeeded);
            if (!succeeded) activity?.SetStatus(ActivityStatusCode.Error);
            await RecordSafelyAsync(succeeded ? TelemetryOutcome.Success : TelemetryOutcome.Failed, started).ConfigureAwait(false);
            return response;
        }
        catch (OperationCanceledException)
        {
            activity?.SetTag("domainrelay.success", false);
            activity?.SetStatus(ActivityStatusCode.Error);
            await RecordSafelyAsync(TelemetryOutcome.Cancelled, started).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            activity?.SetTag("domainrelay.success", false);
            activity?.SetTag("domainrelay.exception.type", exception.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error);
            await RecordSafelyAsync(TelemetryOutcome.Failed, started).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask RecordSafelyAsync(TelemetryOutcome outcome, long started)
    {
        try
        {
            var record = TelemetryRecord.Create(
                $"domainrelay.{typeof(TRequest).Name}",
                "application",
                outcome,
                Stopwatch.GetElapsedTime(started));
            await telemetry.RecordAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must never alter the business operation or leak a secondary failure.
        }
    }
}
