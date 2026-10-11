using System.Security.Cryptography;
using System.Text;
using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>Scopes an API key can be granted.</summary>
public static class ApiScopes
{
    public const string GpsWrite = "gps.write";
    public const string TicketsRead = "tickets.read";
    public const string ExportRead = "export.read";

    public static readonly IReadOnlyList<string> All = [GpsWrite, TicketsRead, ExportRead];
}

/// <summary>
/// Key for machine-to-machine integrations (GPS providers, accounting tools). Only a SHA-256 hash is stored;
/// the full key is shown once when it is created.
/// </summary>
public sealed class ApiKey : TenantAggregateRoot
{
    public const string KeyPrefix = "brk_";

    private List<string> _scopes = [];

    private ApiKey()
    {
    }

    private ApiKey(string name, string prefix, string keyHash)
    {
        Name = name;
        Prefix = prefix;
        KeyHash = keyHash;
    }

    public string Name { get; private set; } = null!;

    /// <summary>First characters of the key, to recognize it in the list.</summary>
    public string Prefix { get; private set; } = null!;

    public string KeyHash { get; private set; } = null!;

    public IReadOnlyList<string> Scopes => _scopes;

    public DateOnly? ExpiresOn { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public bool IsRevoked { get; private set; }

    /// <summary>Creates the key; the plain value is returned only here.</summary>
    public static Result<(ApiKey Key, string PlainValue)> Generate(string name, IEnumerable<string> scopes, DateOnly? expiresOn)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
        {
            return IntegrationErrors.InvalidKeyName;
        }

        var scopeList = scopes.Distinct().ToList();
        if (scopeList.Count == 0 || scopeList.Any(s => !ApiScopes.All.Contains(s)))
        {
            return IntegrationErrors.InvalidScopes;
        }

        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(30)).Replace('+', 'x').Replace('/', 'y').TrimEnd('=');
        var plain = KeyPrefix + secret;
        var key = new ApiKey(name.Trim(), plain[..12], Hash(plain)) { _scopes = scopeList, ExpiresOn = expiresOn };

        return (key, plain);
    }

    public static string Hash(string plainValue) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plainValue))).ToLowerInvariant();

    public bool IsUsable(DateOnly today) => !IsRevoked && (ExpiresOn is null || ExpiresOn >= today);

    public bool HasScope(string scope) => _scopes.Contains(scope);

    public void MarkUsed(DateTimeOffset now) => LastUsedAt = now;

    public void Revoke() => IsRevoked = true;
}

public enum AccountingExportFormat
{
    /// <summary>Generic workbook: one row per movement (account, debit, credit, reference).</summary>
    Excel = 0,

    /// <summary>Same columns as plain text separated by commas.</summary>
    Csv = 1
}

/// <summary>Chart of accounts used to export income vouchers (pólizas) to CONTPAQi or Aspel COI.</summary>
public sealed class AccountingSettings : TenantAggregateRoot
{
    private AccountingSettings()
    {
    }

    public static AccountingSettings CreateDefault() => new()
    {
        CustomersAccount = "105-01-000",
        RevenueAccount = "401-01-000",
        VatTransferredAccount = "208-01-000",
        VatPendingAccount = "209-01-000",
        BankAccount = "102-01-000"
    };

    public string CustomersAccount { get; private set; } = null!;

    public string RevenueAccount { get; private set; } = null!;

    /// <summary>IVA trasladado cobrado.</summary>
    public string VatTransferredAccount { get; private set; } = null!;

    /// <summary>IVA trasladado no cobrado (facturas PPD).</summary>
    public string VatPendingAccount { get; private set; } = null!;

    public string BankAccount { get; private set; } = null!;

    public AccountingExportFormat Format { get; private set; } = AccountingExportFormat.Excel;

    public Result Update(string customers, string revenue, string vatTransferred, string vatPending, string bank, AccountingExportFormat format)
    {
        string[] accounts = [customers, revenue, vatTransferred, vatPending, bank];
        if (accounts.Any(a => string.IsNullOrWhiteSpace(a) || a.Trim().Length > 40))
        {
            return IntegrationErrors.InvalidAccount;
        }

        CustomersAccount = customers.Trim();
        RevenueAccount = revenue.Trim();
        VatTransferredAccount = vatTransferred.Trim();
        VatPendingAccount = vatPending.Trim();
        BankAccount = bank.Trim();
        Format = format;
        return Result.Success();
    }
}

public static class IntegrationErrors
{
    public static readonly Error KeyNotFound = Error.NotFound("Integrations.KeyNotFound", "La llave de API no existe.");

    public static readonly Error InvalidKeyName = Error.Validation("Integrations.InvalidKeyName", "El nombre de la llave es obligatorio (máximo 80 caracteres).");

    public static readonly Error InvalidScopes = Error.Validation("Integrations.InvalidScopes", "Selecciona al menos un alcance válido.");

    public static readonly Error InvalidAccount = Error.Validation("Integrations.InvalidAccount", "Todas las cuentas contables son obligatorias (máximo 40 caracteres).");

    public static readonly Error InvalidApiKey = Error.Forbidden("Integrations.InvalidApiKey", "Llave de API inválida, revocada o sin el alcance requerido.");

    public static readonly Error UnknownDataset = Error.NotFound("Integrations.UnknownDataset", "No existe esa exportación.");
}
