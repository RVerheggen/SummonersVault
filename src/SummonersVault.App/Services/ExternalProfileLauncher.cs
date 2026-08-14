using System.ComponentModel;
using System.Diagnostics;
using SummonersVault.Application.ExternalProfiles;

namespace SummonersVault.App.Services;

internal sealed class ExternalProfileLauncher(Func<ProcessStartInfo, Process?> startProcess) : IExternalProfileLauncher
{
    public ExternalProfileLauncher()
        : this(startInfo => Process.Start(startInfo))
    {
    }

    public ExternalProfileLaunchResult Open(Uri profileUri)
    {
        if (!ExternalProfileLinkBuilder.IsAllowed(profileUri))
        {
            return new(false, "This external profile link is not permitted.");
        }

        try
        {
            using Process? process = startProcess(new(profileUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            return new(true);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return new(false, "Windows could not open this profile in your default browser.");
        }
    }
}
