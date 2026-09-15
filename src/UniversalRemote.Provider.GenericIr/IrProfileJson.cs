using System.Buffers;
using System.Text;
using System.Text.Json;

namespace UniversalRemote.Provider.GenericIr;

/// <summary>Canonical reflection-free serializer used for local import persistence.</summary>
public static class IrProfileJson
{
    public static string Serialize(IrProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var buffer = new ArrayBufferWriter<byte>(4096);
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", profile.Version);
            writer.WriteString("id", profile.Id);
            writer.WriteString("displayName", profile.DisplayName);
            writer.WriteBoolean("verified", profile.Verified);
            writer.WriteString("source", profile.Source);
            writer.WriteNumber("carrierFrequencyHz", profile.CarrierFrequencyHz);
            writer.WritePropertyName("commands");
            writer.WriteStartArray();
            foreach (var command in profile.Commands)
            {
                writer.WriteStartObject();
                writer.WriteString("actionId", command.Action.Id);
                writer.WritePropertyName("patternMicroseconds");
                writer.WriteStartArray();
                foreach (var duration in command.PatternMicroseconds) writer.WriteNumberValue(duration);
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
