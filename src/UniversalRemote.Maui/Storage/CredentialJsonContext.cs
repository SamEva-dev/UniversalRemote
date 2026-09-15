using System.Text.Json.Serialization;
using UniversalRemote.Provider.AndroidTv;
using UniversalRemote.Provider.LG;
using UniversalRemote.Provider.Samsung;

namespace UniversalRemote.Maui.Storage;

[JsonSerializable(typeof(AndroidTvCredentials))]
[JsonSerializable(typeof(LgCredentials))]
[JsonSerializable(typeof(SamsungCredentials))]
internal partial class CredentialJsonContext : JsonSerializerContext { }
