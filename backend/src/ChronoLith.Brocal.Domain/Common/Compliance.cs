namespace ChronoLith.Brocal.Domain.Common;

/// <summary>Traffic light of a document with an expiry date (permits, certifications, courses).</summary>
public enum ComplianceStatus
{
    /// <summary>The document does not expire.</summary>
    NoExpiry = 0,

    /// <summary>Green: valid beyond the warning window.</summary>
    Valid = 1,

    /// <summary>Yellow: expires within the warning window.</summary>
    ExpiringSoon = 2,

    /// <summary>Red: already expired.</summary>
    Expired = 3,

    /// <summary>Required document that has not been captured.</summary>
    Missing = 4
}

public static class ComplianceRules
{
    public const int DefaultWarningDays = 30;

    public static ComplianceStatus Evaluate(DateOnly? expiresOn, DateOnly today, int warningDays = DefaultWarningDays)
    {
        if (expiresOn is null)
        {
            return ComplianceStatus.NoExpiry;
        }

        if (expiresOn.Value < today)
        {
            return ComplianceStatus.Expired;
        }

        return expiresOn.Value.DayNumber - today.DayNumber <= warningDays
            ? ComplianceStatus.ExpiringSoon
            : ComplianceStatus.Valid;
    }

    /// <summary>Days left until expiry (negative when expired), or null when it does not expire.</summary>
    public static int? DaysLeft(DateOnly? expiresOn, DateOnly today) =>
        expiresOn is null ? null : expiresOn.Value.DayNumber - today.DayNumber;

    /// <summary>The worst status of a set (used for a person or a unit with several documents).</summary>
    public static ComplianceStatus Worst(IEnumerable<ComplianceStatus> statuses)
    {
        var worst = ComplianceStatus.NoExpiry;
        foreach (var status in statuses)
        {
            if (Rank(status) > Rank(worst))
            {
                worst = status;
            }
        }

        return worst;
    }

    private static int Rank(ComplianceStatus status) => status switch
    {
        ComplianceStatus.NoExpiry => 0,
        ComplianceStatus.Valid => 1,
        ComplianceStatus.ExpiringSoon => 2,
        ComplianceStatus.Missing => 3,
        ComplianceStatus.Expired => 4,
        _ => 0
    };
}
