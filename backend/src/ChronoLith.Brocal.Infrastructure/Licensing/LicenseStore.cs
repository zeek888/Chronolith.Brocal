using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Persistence;
using ChronoLith.Licensing;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Infrastructure.Licensing;

/// <summary>
/// Installation id and license loaded from the "Licencia" page, in the single row of <c>installation_license</c>
/// (global: no company filter). Standalone mode only.
/// </summary>
internal sealed class LicenseStore(BrocalDbContext db, IUserContext user, TimeProvider timeProvider) : ILicenseStore
{
    public async Task<Guid> GetOrCreateInstanceIdAsync(CancellationToken cancellationToken)
    {
        var existing = await FindAsync(cancellationToken);
        if (existing is not null)
        {
            return existing.InstanceId;
        }

        var created = InstallationLicense.Create(Guid.NewGuid());
        db.InstallationLicenses.Add(created);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return created.InstanceId;
        }
        catch (DbUpdateException)
        {
            // Another replica created the row at the same time (same primary key): its id wins.
            db.Entry(created).State = EntityState.Detached;
            var winner = await FindAsync(cancellationToken);
            return winner?.InstanceId ?? throw new InvalidOperationException("No se pudo registrar el id de la instalación.");
        }
    }

    public async Task<string?> GetStoredTokenAsync(CancellationToken cancellationToken) =>
        (await FindAsync(cancellationToken))?.Token;

    public async Task SaveTokenAsync(string token, CancellationToken cancellationToken)
    {
        var row = await db.InstallationLicenses.FirstOrDefaultAsync(l => l.Id == InstallationLicense.SingletonId, cancellationToken);
        if (row is null)
        {
            row = InstallationLicense.Create(Guid.NewGuid());
            db.InstallationLicenses.Add(row);
        }

        row.Upload(token, timeProvider.GetUtcNow(), user.IsAuthenticated ? user.DisplayName : null);
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task<InstallationLicense?> FindAsync(CancellationToken cancellationToken) =>
        db.InstallationLicenses.AsNoTracking().FirstOrDefaultAsync(l => l.Id == InstallationLicense.SingletonId, cancellationToken);
}

/// <summary>Usage of the limits <c>users</c> and <c>companies</c> shown next to the license.</summary>
internal sealed class LicenseUsageProvider(ILicenseLimits limits) : ILicenseUsageProvider
{
    public Task<IReadOnlyDictionary<string, int>> GetUsageAsync(CancellationToken cancellationToken) => limits.UsageAsync(cancellationToken);
}
