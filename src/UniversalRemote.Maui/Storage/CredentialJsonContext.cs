using System.Text.Json.Serialization;
using UniversalRemote.Remote.Provider.AndroidTv;
using UniversalRemote.Remote.Provider.LG;
using UniversalRemote.Remote.Provider.Samsung;

namespace UniversalRemote.Maui.Storage;

[JsonSerializable(typeof(AndroidTvCredentials))]
[JsonSerializable(typeof(LgCredentials))]
[JsonSerializable(typeof(SamsungCredentials))]
internal partial class CredentialJsonContext : JsonSerializerContext { }
