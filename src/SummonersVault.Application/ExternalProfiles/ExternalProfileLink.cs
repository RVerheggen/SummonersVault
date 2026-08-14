namespace SummonersVault.Application.ExternalProfiles;

public sealed record ExternalProfileLink(
    ExternalProfileProvider Provider,
    string ProviderName,
    Uri Uri);
