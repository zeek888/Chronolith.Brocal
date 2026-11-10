using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Licensing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ChronoLith.Brocal.Application.Features.Platform;

/// <summary>
/// Modules granted by the ChronoLith license of the installation (standalone mode). The license grants a module with
/// the product <c>brocal.&lt;module&gt;</c> (e.g. <c>brocal.tickets</c>); a granted module also grants the modules it
/// depends on, and core modules are always granted. A license without any <c>brocal.*</c> product (only <c>brocal</c>
/// or <c>*</c>) grants every module, and so does module mode, where the Shell validates the license.
/// The effective modules of a company are the ones it contracted that the license grants: the stored selection is
/// kept, so a renewed license with more modules turns them back on without touching the companies.
/// </summary>
public interface ILicensedModules
{
    /// <summary>Granted modules (core and dependencies included); <c>null</c> = every module.</summary>
    IReadOnlySet<string>? Granted { get; }

    bool IsGranted(string module);

    /// <summary>The company with only the contracted modules the license grants.</summary>
    TenantSnapshot Apply(TenantSnapshot tenant);
}

/// <param name="license">Null in module mode (the licensing services are registered only in standalone mode).</param>
internal sealed class LicensedModules(ILogger<LicensedModules>? logger = null, ILicenseAccessor? license = null) : ILicensedModules
{
    /// <summary>Prefix of the license products that grant a module.</summary>
    public const string ProductPrefix = "brocal.";

    private readonly ILogger _logger = logger ?? NullLogger<LicensedModules>.Instance;
    private Cached? _cache;

    public IReadOnlySet<string>? Granted
    {
        get
        {
            if (license is null)
            {
                return null;
            }

            var cache = _cache;
            var version = license.Version;
            if (cache is not null && cache.Version == version)
            {
                return cache.Modules;
            }

            LicenseDocument document;
            try
            {
                document = license.Current.Document;
            }
            catch (InvalidOperationException)
            {
                // Not initialized yet (the database is migrated and seeded before the license is loaded).
                return null;
            }

            var (modules, unknown) = From(document);
            if (unknown.Count > 0)
            {
                _logger.LogWarning("La licencia {LicenseId} concede módulos que Brocal no conoce: {Modules}.", document.Id, string.Join(", ", unknown));
            }

            _cache = new Cached(version, modules);
            return modules;
        }
    }

    public bool IsGranted(string module) =>
        BrocalModules.CoreModules.Contains(module) || Granted is not { } granted || granted.Contains(module);

    public TenantSnapshot Apply(TenantSnapshot tenant) =>
        Granted is { } granted && tenant.EnabledModules.Any(m => !granted.Contains(m))
            ? tenant with { EnabledModules = tenant.EnabledModules.Where(granted.Contains).ToList() }
            : tenant;

    /// <summary>Granted modules of a license (<c>null</c> = every module) and the <c>brocal.*</c> codes Brocal does not know.</summary>
    internal static (IReadOnlySet<string>? Modules, IReadOnlyList<string> Unknown) From(LicenseDocument document)
    {
        var listed = document.Products.Keys
            .Where(k => k.StartsWith(ProductPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[ProductPrefix.Length..].Trim().ToLowerInvariant())
            .ToList();

        if (listed.Count == 0)
        {
            return (null, []);
        }

        var unknown = listed.Where(m => !BrocalModules.Exists(m)).ToList();
        return (BrocalModules.WithDependencies(listed).ToHashSet(StringComparer.Ordinal), unknown);
    }

    private sealed record Cached(long Version, IReadOnlySet<string>? Modules);
}

public static class LicensedModuleErrors
{
    public static Error NotGranted(IEnumerable<string> modules) => Error.Forbidden(
        "License.ModuleNotGranted",
        $"La licencia de la instalación no incluye: {string.Join(", ", modules.Select(m => BrocalModules.Find(m)?.Name ?? m))}. Pide a ChronoLith una licencia que los incluya.");
}
