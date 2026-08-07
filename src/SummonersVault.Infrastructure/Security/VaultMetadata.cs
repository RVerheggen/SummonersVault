using System.Text.Json.Serialization;

namespace SummonersVault.Infrastructure.Security;

public sealed record VaultMetadata(
    int FormatVersion,
    Guid VaultId,
    ArgonMetadata Kdf,
    KeyEnvelopeMetadata KeyEnvelope);

public sealed record ArgonMetadata(
    string Algorithm,
    long MemoryBytes,
    long Passes,
    int Parallelism,
    string SaltBase64);

public sealed record KeyEnvelopeMetadata(
    string Algorithm,
    string NonceBase64,
    string CiphertextBase64,
    string TagBase64);

[JsonSerializable(typeof(VaultMetadata))]
internal sealed partial class VaultMetadataJsonContext : JsonSerializerContext;

