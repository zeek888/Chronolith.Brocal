using ChronoLith.Brocal.Application.Abstractions;

namespace ChronoLith.Brocal.Application.Common;

/// <summary>Dates are business dates of the company: "today" follows its time zone (America/Mexico_City by default).</summary>
internal sealed class TenantClock(TimeProvider timeProvider, ITenantContext tenantContext) : ITenantClock
{
    private const string DefaultZone = "America/Mexico_City";

    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();

    public TimeZoneInfo TimeZone => Resolve(tenantContext.HasTenant ? tenantContext.Tenant.Settings.TimeZone : DefaultZone);

    public DateTimeOffset LocalNow => System.TimeZoneInfo.ConvertTime(UtcNow, TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);

    public DateTimeOffset StartOfDay(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static TimeZoneInfo Resolve(string zone)
    {
        try
        {
            return System.TimeZoneInfo.FindSystemTimeZoneById(zone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return System.TimeZoneInfo.FindSystemTimeZoneById(DefaultZone);
        }
    }
}
