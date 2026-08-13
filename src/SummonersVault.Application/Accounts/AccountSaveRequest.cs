using SummonersVault.Application.Security;
using SummonersVault.Core.Models;

namespace SummonersVault.Application.Accounts;

public sealed record AccountSaveRequest(VaultAccount Account, SensitiveBuffer? Password);

public sealed record AccountImportItem(VaultAccount Account, SensitiveBuffer Password);
