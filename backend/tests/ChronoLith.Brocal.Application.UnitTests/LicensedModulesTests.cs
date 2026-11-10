using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Licensing;

namespace ChronoLith.Brocal.Application.UnitTests;

/// <summary>Modules granted by the license of the installation (products <c>brocal.&lt;module&gt;</c>).</summary>
public sealed class LicensedModulesTests
{
    [Fact]
    public void Without_brocal_modules_in_the_license_every_module_is_granted()
    {
        var modules = new LicensedModules(license: new FakeLicense(Document("brocal")));

        Assert.Null(modules.Granted);
        Assert.True(modules.IsGranted(BrocalModules.Invoicing));
        Assert.Null(new LicensedModules(license: new FakeLicense(Document("*"))).Granted);
        Assert.Null(new LicensedModules().Granted);
    }

    [Fact]
    public void A_granted_module_brings_its_dependencies_and_the_core_modules()
    {
        var modules = new LicensedModules(license: new FakeLicense(Document("brocal", "brocal.dispatch")));

        Assert.True(modules.IsGranted(BrocalModules.Dispatch));
        Assert.True(modules.IsGranted(BrocalModules.Tickets));
        Assert.True(modules.IsGranted(BrocalModules.Pricing));
        Assert.True(modules.IsGranted(BrocalModules.Fleet));
        Assert.True(modules.IsGranted(BrocalModules.Crews));
        Assert.True(modules.IsGranted(BrocalModules.Personnel));
        Assert.True(modules.IsGranted(BrocalModules.Catalogs));
        Assert.True(modules.IsGranted(BrocalModules.Automation));
        Assert.False(modules.IsGranted(BrocalModules.Invoicing));
        Assert.False(modules.IsGranted(BrocalModules.Hse));
    }

    [Fact]
    public void Unknown_codes_are_reported_and_ignored()
    {
        var (granted, unknown) = LicensedModules.From(Document("brocal", "brocal.tickets", "brocal.boletas"));

        Assert.Equal(["boletas"], unknown);
        Assert.Contains(BrocalModules.Tickets, granted!);
        Assert.DoesNotContain("boletas", granted!);
    }

    [Fact]
    public void A_company_keeps_only_the_contracted_modules_the_license_grants_and_a_new_license_turns_them_back_on()
    {
        var license = new FakeLicense(Document("brocal", "brocal.tickets"));
        var modules = new LicensedModules(license: license);
        var tenant = TenantSnapshot.From(Tenant.Create("servicios-golfo", "Servicios del Golfo", "Profesional", [BrocalModules.Tickets, BrocalModules.Invoicing]).Value);

        var restricted = modules.Apply(tenant);
        Assert.True(restricted.IsModuleEnabled(BrocalModules.Tickets));
        Assert.False(restricted.IsModuleEnabled(BrocalModules.Invoicing));
        Assert.Contains(BrocalModules.Invoicing, tenant.EnabledModules);

        license.Activate(Document("brocal"));
        Assert.True(modules.Apply(tenant).IsModuleEnabled(BrocalModules.Invoicing));
    }

    [Fact]
    public void Before_the_license_is_loaded_nothing_is_restricted()
    {
        var modules = new LicensedModules(license: new FakeLicense(null));

        Assert.Null(modules.Granted);
    }

    private static LicenseDocument Document(params string[] products) => new()
    {
        Id = Guid.NewGuid(),
        Customer = "Pruebas",
        IssuedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30),
        Products = products.ToDictionary(p => p, _ => ProductGrant.Unlimited)
    };

    private sealed class FakeLicense(LicenseDocument? document) : ILicenseAccessor
    {
        private LicenseDocument? _document = document;

        public long Version { get; private set; } = 1;

        public LicenseSnapshot Current => _document is { } current
            ? new LicenseSnapshot(current, LicenseStatus.Evaluate(current, DateOnly.FromDateTime(DateTime.UtcNow)), Guid.Empty, "brocal", LicenseSource.Configuration, Version)
            : throw new InvalidOperationException("La licencia no se ha inicializado.");

        public void Activate(LicenseDocument next)
        {
            _document = next;
            Version++;
        }

        public Task<LicenseProblem?> ApplyAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
