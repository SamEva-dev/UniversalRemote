using System.Collections.Frozen;
using UniversalRemote.Abstractions;

namespace UniversalRemote.Provider.GenericIr;

public sealed class IrCommand
{
    public RemoteAction Action { get; }
    public IReadOnlyList<int> PatternMicroseconds { get; }

    public IrCommand(RemoteAction action, IEnumerable<int> patternMicroseconds)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(patternMicroseconds);
        var snapshot = patternMicroseconds.ToArray();
        if (!InfraredSignalLimits.IsPatternValid(snapshot))
            throw new ArgumentException("Invalid infrared pattern.", nameof(patternMicroseconds));
        Action = action;
        PatternMicroseconds = Array.AsReadOnly(snapshot);
    }
}

public sealed class IrProfile
{
    private readonly FrozenDictionary<string, IrCommand> commands;

    public int Version { get; }
    public string Id { get; }
    public string DisplayName { get; }
    public bool Verified { get; }
    public string Source { get; }
    public int CarrierFrequencyHz { get; }
    public IReadOnlyCollection<RemoteAction> Capabilities { get; }
    public IReadOnlyList<IrCommand> Commands { get; }

    public IrProfile(
        int version,
        string id,
        string displayName,
        bool verified,
        string source,
        int carrierFrequencyHz,
        IEnumerable<IrCommand> commands)
    {
        if (version != IrProfileParser.CurrentVersion) throw new ArgumentOutOfRangeException(nameof(version));
        ValidateIdentifier(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.Length > 120) throw new ArgumentOutOfRangeException(nameof(displayName));
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (source.Length > 240) throw new ArgumentOutOfRangeException(nameof(source));
        if (!InfraredSignalLimits.IsCarrierFrequencyPlausible(carrierFrequencyHz))
            throw new ArgumentOutOfRangeException(nameof(carrierFrequencyHz));
        ArgumentNullException.ThrowIfNull(commands);

        var snapshot = commands.ToArray();
        if (snapshot.Length == 0 || snapshot.Length > IrProfileParser.MaxCommands)
            throw new ArgumentException("An IR profile must contain between 1 and 128 commands.", nameof(commands));
        if (snapshot.Any(command => command is null))
            throw new ArgumentException("Null IR command.", nameof(commands));

        var dictionary = new Dictionary<string, IrCommand>(StringComparer.Ordinal);
        foreach (var command in snapshot)
            if (!dictionary.TryAdd(command.Action.Id, command))
                throw new ArgumentException("Duplicate IR action.", nameof(commands));

        Version = version;
        Id = id;
        DisplayName = displayName;
        Verified = verified;
        Source = source;
        CarrierFrequencyHz = carrierFrequencyHz;
        this.commands = dictionary.ToFrozenDictionary(StringComparer.Ordinal);
        Commands = Array.AsReadOnly(snapshot);
        Capabilities = Array.AsReadOnly(snapshot.Select(command => command.Action).ToArray());
    }

    public bool TryGetCommand(RemoteAction action, out IrCommand command)
    {
        ArgumentNullException.ThrowIfNull(action);
        return commands.TryGetValue(action.Id, out command!);
    }

    internal static void ValidateIdentifier(string id, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id, parameterName);
        if (id.Length > 100 || id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')))
            throw new ArgumentException("Invalid IR profile identifier.", parameterName);
    }
}
