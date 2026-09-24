using System.Text.Json.Serialization;

namespace Nat64Switch;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(StatusPayload))]
[JsonSerializable(typeof(Dictionary<string, BackupState>))]
internal partial class AppJsonContext : JsonSerializerContext;
