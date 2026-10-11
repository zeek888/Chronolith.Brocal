using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;

namespace ChronoLith.Brocal.Domain.UnitTests;

public sealed class ComplianceRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Theory]
    [InlineData(null, ComplianceStatus.NoExpiry)]
    [InlineData(-1, ComplianceStatus.Expired)]
    [InlineData(0, ComplianceStatus.ExpiringSoon)]
    [InlineData(30, ComplianceStatus.ExpiringSoon)]
    [InlineData(31, ComplianceStatus.Valid)]
    public void Traffic_light_uses_the_warning_window(int? daysToExpire, ComplianceStatus expected)
    {
        DateOnly? expiresOn = daysToExpire is null ? null : Today.AddDays(daysToExpire.Value);

        Assert.Equal(expected, ComplianceRules.Evaluate(expiresOn, Today, warningDays: 30));
    }

    [Fact]
    public void Days_left_are_negative_once_expired()
    {
        Assert.Equal(-5, ComplianceRules.DaysLeft(Today.AddDays(-5), Today));
        Assert.Null(ComplianceRules.DaysLeft(null, Today));
    }

    [Fact]
    public void Worst_status_ranks_expired_over_missing_over_expiring()
    {
        Assert.Equal(ComplianceStatus.Expired, ComplianceRules.Worst([ComplianceStatus.Valid, ComplianceStatus.Missing, ComplianceStatus.Expired]));
        Assert.Equal(ComplianceStatus.Missing, ComplianceRules.Worst([ComplianceStatus.ExpiringSoon, ComplianceStatus.Missing]));
        Assert.Equal(ComplianceStatus.NoExpiry, ComplianceRules.Worst([]));
    }
}

public sealed class PriceResolverTests
{
    private static readonly Guid Service = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Contract = Guid.NewGuid();
    private static readonly DateOnly Date = new(2026, 10, 1);

    private static PriceList List(string code, decimal price, Guid? customer = null, Guid? contract = null, DateOnly? from = null, DateOnly? to = null) =>
        PriceList.Create(new PriceListData(code, code, customer, contract, "MXN", from ?? new DateOnly(2026, 1, 1), to, [new PriceListItemData(Service, price, null)], null)).Value;

    [Fact]
    public void Contract_price_beats_customer_and_general_lists()
    {
        var lists = new[] { List("GRAL", 100), List("CLIENTE", 90, Customer), List("CONTRATO", 80, Customer, Contract) };

        var price = PriceResolver.Resolve(lists, Customer, Contract, Service, Date, 120);

        Assert.Equal(80, price.UnitPrice);
        Assert.Equal(PriceSource.Contract, price.Source);
    }

    [Fact]
    public void Customer_price_applies_when_the_ticket_has_no_contract()
    {
        var lists = new[] { List("GRAL", 100), List("CLIENTE", 90, Customer), List("CONTRATO", 80, Customer, Contract) };

        var price = PriceResolver.Resolve(lists, Customer, null, Service, Date, 120);

        Assert.Equal(90, price.UnitPrice);
        Assert.Equal(PriceSource.Customer, price.Source);
    }

    [Fact]
    public void Lists_of_other_customers_and_expired_lists_are_ignored()
    {
        var lists = new[] { List("OTRO", 50, Guid.NewGuid()), List("VENCIDO", 60, Customer, to: new DateOnly(2026, 6, 30)), List("GRAL", 100) };

        var price = PriceResolver.Resolve(lists, Customer, null, Service, Date, 120);

        Assert.Equal(100, price.UnitPrice);
        Assert.Equal(PriceSource.General, price.Source);
    }

    [Fact]
    public void Falls_back_to_the_base_price_and_then_to_none()
    {
        Assert.Equal(new ResolvedPrice(120, PriceSource.BasePrice, null, null), PriceResolver.Resolve([], Customer, null, Service, Date, 120));
        Assert.Equal(PriceSource.None, PriceResolver.Resolve([], Customer, null, Service, Date, null).Source);
    }

    [Fact]
    public void A_contract_list_requires_its_customer()
    {
        var result = PriceList.Create(new PriceListData("X", "X", null, Contract, "MXN", Date, null, [], null));

        Assert.True(result.IsFailure);
        Assert.Equal(PricingErrors.ContractWithoutCustomer, result.Error);
    }
}
