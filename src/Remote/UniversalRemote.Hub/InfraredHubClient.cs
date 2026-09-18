using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Hub;

/// <summary>Transport-independent facade over one external infrared hub.</summary>
public sealed class InfraredHubClient : IInfraredHub
{
    private readonly IInfraredHubTransport _transport;

    public InfraredHubId Id { get; }
    public InfraredHubTransportKind TransportKind => _transport.Kind;

    public InfraredHubClient(InfraredHubId id, IInfraredHubTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        if (string.IsNullOrWhiteSpace(id.Value))
            throw new ArgumentException("Hub ID must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(transport.TransportId))
            throw new ArgumentException("TransportId must not be empty.", nameof(transport));

        Id = id;
        _transport = transport;
    }

    public ValueTask<InfraredHubInfo> GetInfoAsync(CancellationToken cancellationToken = default)
        => _transport.GetInfoAsync(Id, cancellationToken);

    public Task<InfraredHubTransmitResult> TransmitAsync(InfraredSignal signal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return _transport.TransmitAsync(Id, signal, cancellationToken);
    }

    public Task<InfraredHubLearnResult> LearnAsync(CancellationToken cancellationToken = default)
        => _transport.LearnAsync(Id, cancellationToken);
}
