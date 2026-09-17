using System.Text.Json;
using UniversalRemote.Remote.Provider.LG;
using UniversalRemote.Remote.Provider.Samsung;

if (JsonSerializer.IsReflectionEnabledByDefault) throw new InvalidOperationException("Reflection must remain disabled.");
using var registration = JsonDocument.Parse(LgProtocol.Register("key\"\\\nété"));
if (registration.RootElement.GetProperty("payload").GetProperty("client-key").GetString() != "key\"\\\nété")
    throw new InvalidOperationException("LG registration mismatch.");
foreach (bool? mute in new bool?[] { null, true, false })
{
    using var request = JsonDocument.Parse(LgProtocol.Request("req_1", "ssap://audio/setMute", mute));
    var payload = request.RootElement.GetProperty("payload");
    if (mute is { } expected)
    {
        if (payload.GetProperty("mute").GetBoolean() != expected) throw new InvalidOperationException("LG mute mismatch.");
    }
    else if (payload.EnumerateObject().Any()) throw new InvalidOperationException("Expected empty LG payload.");
}
using var samsung = JsonDocument.Parse(SamsungProtocol.KeyMessage("KEY_\"\\\nété"));
var parameters = samsung.RootElement.GetProperty("params");
if (parameters.GetProperty("DataOfCmd").GetString() != "KEY_\"\\\nété" || parameters.GetProperty("Option").GetString() != "false")
    throw new InvalidOperationException("Samsung wire contract mismatch.");
Console.WriteLine("LG + Samsung JSON: OK; reflection disabled; no network command sent.");
