using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Operations;

namespace ChronoLith.Brocal.Domain.UnitTests;

public sealed class ServiceTicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 17, 0, 0, TimeSpan.FromHours(-6));

    private static readonly ServiceType FluidTransport = ServiceType.Create(new ServiceTypeData("TF", "Transporte de fluidos", null, null, null, RequiresSignature: true,
        MinPhotos: 1, [new FormFieldDefinition("volumen", "Volumen", FormFieldType.Number, Required: true, null, "m3", 1)], null, null)).Value;

    private static ServiceTicket NewTicket(string? volume = "64", decimal quantity = 10)
    {
        var data = new TicketData(Guid.NewGuid(), null, Guid.NewGuid(), FluidTransport.Id, null, DateOnly.FromDateTime(Now.Date), null, null, [Guid.NewGuid()], null, [],
            "Transporte de agua congénita", null, "Ing. Rocío Pérez", new Dictionary<string, string?> { ["volumen"] = volume }, null);
        return ServiceTicket.Create(Guid.CreateVersion7(), "BS-000001", 1, Guid.NewGuid(), data,
            [new PricedLine(Guid.NewGuid(), "Transporte de fluidos", quantity, "m3", 168.5m, PriceSource.Contract, 0.16m)], null, "Operador", "MXN").Value;
    }

    private static ServiceTicket SignedTicket()
    {
        var ticket = NewTicket();
        ticket.AddPhoto(Guid.NewGuid(), "Llegada a locación", Now, null);
        Assert.True(ticket.Sign(FluidTransport, 0, "Ing. Rocío Pérez", "Residente", Guid.NewGuid(), Now, null).IsSuccess);
        return ticket;
    }

    [Fact]
    public void Totals_come_from_the_frozen_prices()
    {
        var ticket = NewTicket(quantity: 10);

        Assert.Equal(1685.00m, ticket.Subtotal);
        Assert.Equal(269.60m, ticket.Vat);
        Assert.Equal(1954.60m, ticket.Total);
    }

    [Fact]
    public void Signing_requires_the_minimum_photos_and_a_complete_form()
    {
        var withoutPhoto = NewTicket();
        Assert.Equal("Tickets.MissingPhotos", withoutPhoto.Sign(FluidTransport, 0, "Residente", null, Guid.NewGuid(), Now, null).Error.Code);

        var incomplete = NewTicket(volume: null);
        incomplete.AddPhoto(Guid.NewGuid(), null, Now, null);
        Assert.Equal("ServiceTypes.MissingAnswer", incomplete.Sign(FluidTransport, 0, "Residente", null, Guid.NewGuid(), Now, null).Error.Code);
    }

    [Fact]
    public void A_signed_ticket_cannot_be_edited_until_reopened_with_a_reason()
    {
        var ticket = SignedTicket();

        Assert.Equal(TicketStatus.Signed, ticket.Status);
        Assert.Equal(TicketErrors.NotEditable, ticket.AddPhoto(Guid.NewGuid(), null, Now, null).Error);
        Assert.Equal(TicketErrors.ReasonRequired, ticket.Reopen(" ").Error);

        Assert.True(ticket.Reopen("El cliente corrigió el volumen").IsSuccess);
        Assert.Equal(TicketStatus.Draft, ticket.Status);
        Assert.Null(ticket.SignatureFileId);
    }

    [Fact]
    public void Invoicing_requires_approval_when_the_company_asks_for_it()
    {
        var ticket = SignedTicket();

        Assert.Equal(TicketErrors.ApprovalRequired, ticket.EnsureInvoiceable(requireApproval: true).Error);
        Assert.True(ticket.EnsureInvoiceable(requireApproval: false).IsSuccess);

        ticket.Approve("Oficina", Now);
        Assert.True(ticket.MarkInvoiced(Guid.NewGuid(), Now, requireApproval: true).IsSuccess);
        Assert.Equal(TicketStatus.Invoiced, ticket.Status);
    }

    [Fact]
    public void An_invoiced_ticket_is_cancelled_only_after_its_invoice()
    {
        var ticket = SignedTicket();
        ticket.Approve("Oficina", Now);
        ticket.MarkInvoiced(Guid.NewGuid(), Now, requireApproval: true);

        Assert.Equal(TicketErrors.CancelInvoiceFirst, ticket.Cancel("Error de captura", Now).Error);

        ticket.ReleaseInvoice();
        Assert.Equal(TicketStatus.Approved, ticket.Status);
        Assert.True(ticket.Cancel("Error de captura", Now).IsSuccess);
        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
    }

    [Fact]
    public void Closing_without_signature_is_rejected_when_the_service_type_requires_it()
    {
        var ticket = NewTicket();

        Assert.Equal(TicketErrors.SignatureRequired, ticket.CloseWithoutSignature(FluidTransport, 0, tenantRequiresSignature: false, Now).Error);
    }

    [Fact]
    public void Operators_see_the_tickets_they_captured_or_took_part_in()
    {
        var member = Guid.NewGuid();
        var employee = Guid.NewGuid();
        var data = new TicketData(Guid.NewGuid(), null, Guid.NewGuid(), FluidTransport.Id, null, new DateOnly(2026, 10, 1), null, null, [], null, [employee], null, null, null, null, null);
        var ticket = ServiceTicket.Create(Guid.CreateVersion7(), "BS-000002", 2, null, data, [], member, "Operador", "MXN").Value;

        Assert.True(ticket.Involves(member, null));
        Assert.True(ticket.Involves(null, employee));
        Assert.False(ticket.Involves(Guid.NewGuid(), Guid.NewGuid()));
    }
}
