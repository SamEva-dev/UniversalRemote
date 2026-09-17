using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalRemote.Remote.Abstractions;

namespace UniversalRemote.Remote.Provider.GenericIr;

public static class IrProfileParser
{
    public const int CurrentVersion = 1;
    public const int MaxJsonBytes = 128 * 1024;
    public const int MaxCommands = 128;

    public static IrProfile Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new FormatException("IR profile exceeds the maximum JSON size.");

        IrProfileDocument document;
        try
        {
            document = JsonSerializer.Deserialize(json, IrJsonContext.Default.IrProfileDocument)
                ?? throw new FormatException("IR profile is empty.");
        }
        catch (JsonException exception)
        {
            throw new FormatException("Invalid IR profile JSON.", exception);
        }

        if (document.Version != CurrentVersion) throw new FormatException("Unsupported IR profile version.");
        if (document.Commands is null || document.Commands.Count == 0 || document.Commands.Count > MaxCommands)
            throw new FormatException("IR profile command count is invalid.");
        if (string.IsNullOrWhiteSpace(document.Id) || string.IsNullOrWhiteSpace(document.DisplayName) || string.IsNullOrWhiteSpace(document.Source))
            throw new FormatException("IR profile metadata is incomplete.");
        if (!InfraredSignalLimits.IsCarrierFrequencyPlausible(document.CarrierFrequencyHz))
            throw new FormatException("IR carrier frequency is invalid.");

        try
        {
            IrProfile.ValidateIdentifier(document.Id, nameof(document.Id));
            var commands = document.Commands.Select(command =>
            {
                if (string.IsNullOrWhiteSpace(command.ActionId) || command.PatternMicroseconds is null)
                    throw new FormatException("IR command is incomplete.");
                var action = new RemoteAction(command.ActionId);
                if (!InfraredSignalLimits.IsPatternValid(command.PatternMicroseconds))
                    throw new FormatException("IR command pattern is invalid.");
                return new IrCommand(action, command.PatternMicroseconds);
            }).ToArray();

            return new IrProfile(
                document.Version,
                document.Id,
                document.DisplayName,
                document.Verified,
                document.Source,
                document.CarrierFrequencyHz,
                commands);
        }
        catch (FormatException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new FormatException("IR profile contains invalid values.", exception);
        }
    }

    public static IrProfile Parse(Stream jsonStream)
    {
        ArgumentNullException.ThrowIfNull(jsonStream);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = jsonStream.Read(chunk, 0, chunk.Length);
            if (read == 0) break;
            if (buffer.Length + read > MaxJsonBytes)
                throw new FormatException("IR profile exceeds the maximum JSON size.");
            buffer.Write(chunk, 0, read);
        }
        return Parse(Encoding.UTF8.GetString(buffer.ToArray()));
    }
}

internal sealed class IrProfileDocument
{
    public int Version { get; set; }
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool Verified { get; set; }
    public string Source { get; set; } = string.Empty;
    public int CarrierFrequencyHz { get; set; }
    public List<IrCommandDocument>? Commands { get; set; }
}

internal sealed class IrCommandDocument
{
    public string ActionId { get; set; } = string.Empty;
    public int[]? PatternMicroseconds { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(IrProfileDocument))]
internal partial class IrJsonContext : JsonSerializerContext { }
