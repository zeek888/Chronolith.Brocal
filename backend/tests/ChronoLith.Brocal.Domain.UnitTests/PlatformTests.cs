using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Platform;

namespace ChronoLith.Brocal.Domain.UnitTests;

public sealed class ModuleCatalogTests
{
    [Fact]
    public void Catalog_has_the_24_requested_modules_plus_core_master_data()
    {
        Assert.Equal(25, BrocalModules.All.Count);
        Assert.Equal(Enumerable.Range(0, 25), BrocalModules.All.Select(m => m.Number).Order());
    }

    [Fact]
    public void Permission_codes_belong_to_their_module()
    {
        foreach (var module in BrocalModules.All)
        {
            Assert.All(module.Permissions, p => Assert.Equal(module.Code, p.ModuleCode));
        }
    }

    [Fact]
    public void Enabling_a_module_enables_core_modules_and_every_dependency()
    {
        foreach (var module in BrocalModules.All)
        {
            var enabled = BrocalModules.WithDependencies([module.Code]);

            Assert.Contains(module.Code, enabled);
            Assert.All(BrocalModules.CoreModules, core => Assert.Contains(core, enabled));
            Assert.All(enabled, code => Assert.All(BrocalModules.Find(code)!.DependsOn, dependency => Assert.Contains(dependency, enabled)));
        }
    }

    [Fact]
    public void Unknown_modules_are_ignored()
    {
        Assert.Equal(BrocalModules.CoreModules.Order(), BrocalModules.WithDependencies(["no-existe"]).Order());
    }

    [Fact]
    public void Role_templates_only_use_known_permissions()
    {
        Assert.All(RoleTemplates.All.SelectMany(t => t.Permissions), p => Assert.True(BrocalModules.IsPermission(p), p));
    }
}

public sealed class IntegrationTests
{
    [Fact]
    public void Api_keys_store_only_the_hash_and_check_scopes()
    {
        var (key, plain) = ApiKey.Generate("GPS", [ApiScopes.GpsWrite], null).Value;

        Assert.StartsWith(ApiKey.KeyPrefix, plain);
        Assert.Equal(ApiKey.Hash(plain), key.KeyHash);
        Assert.DoesNotContain(plain, key.KeyHash, StringComparison.Ordinal);
        Assert.True(key.HasScope(ApiScopes.GpsWrite));
        Assert.False(key.HasScope(ApiScopes.ExportRead));
    }

    [Fact]
    public void Revoked_or_expired_keys_are_not_usable()
    {
        var today = new DateOnly(2026, 10, 11);
        var (expiring, _) = ApiKey.Generate("Contabilidad", [ApiScopes.ExportRead], today.AddDays(-1)).Value;
        var (revoked, _) = ApiKey.Generate("GPS", [ApiScopes.GpsWrite], null).Value;
        revoked.Revoke();

        Assert.False(expiring.IsUsable(today));
        Assert.False(revoked.IsUsable(today));
    }

    [Fact]
    public void Unknown_scopes_are_rejected()
    {
        Assert.True(ApiKey.Generate("X", ["admin.all"], null).IsFailure);
    }

    [Fact]
    public void Outbox_retries_with_exponential_back_off_until_it_gives_up()
    {
        var now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
        var message = new OutboxMessage(OutboxChannel.Email, "residente@pemex-demo.mx", "Boleta firmada", "...", NotificationEvent.TicketSigned, null, now);

        message.MarkFailed(now, "SMTP 421");
        Assert.Equal(now.AddMinutes(1), message.NextAttemptAt);
        message.MarkFailed(now, "SMTP 421");
        Assert.Equal(now.AddMinutes(2), message.NextAttemptAt);

        for (var i = 2; i < OutboxMessage.MaxAttempts; i++)
        {
            message.MarkFailed(now, "SMTP 421");
        }

        Assert.Equal(OutboxStatus.Failed, message.Status);
    }
}

public sealed class IdentifierTests
{
    [Theory]
    [InlineData("PEP920716AB1", true)]
    [InlineData("GODE561231GR8", true)]
    [InlineData(MexicanIds.GenericRfc, true)]
    [InlineData("PEP921316AB1", false)]
    [InlineData("PE920716AB1", false)]
    public void Rfc_follows_the_sat_format(string rfc, bool valid) => Assert.Equal(valid, MexicanIds.IsValidRfc(rfc));

    [Fact]
    public void Folios_are_padded_with_the_company_prefix() => Assert.Equal("BS-000123", FolioSequence.Format("BS", 123));

    [Fact]
    public void Money_rounds_half_away_from_zero() => Assert.Equal(2.35m, Money.Round(2.345m));

    [Theory]
    [InlineData("AT-01", true)]
    [InlineData("SEN-101/A", true)]
    [InlineData("at-01", false)]
    [InlineData("-AT", false)]
    public void Business_codes_are_upper_case(string code, bool valid) => Assert.Equal(valid, Codes.IsValid(code));
}
