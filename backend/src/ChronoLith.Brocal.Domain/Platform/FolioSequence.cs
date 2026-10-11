using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>
/// Consecutive counter per company and document type (tickets, work orders, quotes…). Numbers are taken
/// inside the same transaction that saves the document, so a rollback does not leave gaps.
/// </summary>
public sealed class FolioSequence : Entity, ITenantOwned
{
    private FolioSequence()
    {
    }

    public FolioSequence(Guid tenantId, string key, long nextValue = 1)
    {
        TenantId = tenantId;
        Key = key;
        NextValue = nextValue;
    }

    public Guid TenantId { get; private set; }

    /// <summary>Document type (<c>ticket</c>, <c>work-order</c>, <c>invoice:A</c>…).</summary>
    public string Key { get; private set; } = null!;

    public long NextValue { get; private set; }

    /// <summary>Formats a folio: prefix + 6 digits (<c>BS-000123</c>).</summary>
    public static string Format(string prefix, long number) =>
        string.IsNullOrWhiteSpace(prefix) ? number.ToString("D6") : $"{prefix}-{number:D6}";
}

/// <summary>Keys of the folio sequences.</summary>
public static class FolioKeys
{
    public const string Ticket = "ticket";
    public const string WorkOrder = "work-order";
    public const string Quote = "quote";
    public const string Maintenance = "maintenance";
    public const string Incident = "incident";
    public const string Trip = "trip";
    public const string Estimate = "estimate";
    public const string Expense = "expense";

    public static string Invoice(string series) => $"invoice:{series}";

    public static string Payment(string series) => $"payment:{series}";
}
