namespace SummonersVault.Application.ExternalProfiles;

public interface IExternalProfileLauncher
{
    ExternalProfileLaunchResult Open(Uri profileUri);
}

public sealed record ExternalProfileLaunchResult(bool Succeeded, string? ErrorMessage = null);
