using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Features.Automation;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Tickets;

// ---- Photos ---------------------------------------------------------------------------------------------

public sealed record AddTicketPhotoCommand(Guid TicketId, Guid FileId, string? Caption, DateTimeOffset? TakenAt, GeoPoint? Location) : ICommand<TicketPhotoDto>;

internal sealed class AddTicketPhotoCommandHandler(IApplicationDbContext db, ITenantContext tenant, IFileService files, ITenantClock clock)
    : ICommandHandler<AddTicketPhotoCommand, TicketPhotoDto>
{
    public async Task<Result<TicketPhotoDto>> Handle(AddTicketPhotoCommand command, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.Visible(tenant).Include(t => t.Photos).FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        var file = await db.Files.FirstOrDefaultAsync(f => f.Id == command.FileId, cancellationToken);
        if (file is null)
        {
            return FileErrors.NotFound;
        }

        if (!StoredFile.IsImage(file.ContentType))
        {
            return FileErrors.NotAnImage;
        }

        var result = ticket.AddPhoto(file.Id, command.Caption, command.TakenAt ?? clock.UtcNow, command.Location);
        if (result.IsFailure)
        {
            return result.Error;
        }

        file.AttachTo(nameof(ServiceTicket), ticket.Id);
        await db.SaveChangesAsync(cancellationToken);

        var photo = ticket.Photos.First(p => p.FileId == file.Id);
        return new TicketPhotoDto(photo.Id, photo.FileId, files.GetUrl(photo.FileId), photo.Caption, photo.TakenAt, photo.Latitude, photo.Longitude);
    }
}

public sealed record RemoveTicketPhotoCommand(Guid TicketId, Guid PhotoId) : ICommand;

internal sealed class RemoveTicketPhotoCommandHandler(IApplicationDbContext db, ITenantContext tenant) : ICommandHandler<RemoveTicketPhotoCommand>
{
    public async Task<Result> Handle(RemoveTicketPhotoCommand command, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.Visible(tenant).Include(t => t.Photos).FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        var result = ticket.RemovePhoto(command.PhotoId);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

// ---- Sign / close ---------------------------------------------------------------------------------------

/// <summary>Closes the ticket with the customer's signature (PNG drawn on the device, uploaded before).</summary>
public sealed record SignTicketCommand(Guid TicketId, string SignerName, string? SignerPosition, Guid SignatureFileId, DateTimeOffset? SignedAt, GeoPoint? Location)
    : ICommand<TicketDetailDto>;

internal sealed class SignTicketCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITenantClock clock,
    TicketClosing closing,
    DocumentFacts documentFacts,
    IBusinessRules rules) : ICommandHandler<SignTicketCommand, TicketDetailDto>
{
    public async Task<Result<TicketDetailDto>> Handle(SignTicketCommand command, CancellationToken cancellationToken)
    {
        var ticket = await TicketLoader.LoadAsync(db, tenant, command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        var signature = await db.Files.FirstOrDefaultAsync(f => f.Id == command.SignatureFileId, cancellationToken);
        if (signature is null || !StoredFile.IsImage(signature.ContentType))
        {
            return FileErrors.NotAnImage;
        }

        var type = await db.ServiceTypes.AsNoTracking().FirstAsync(t => t.Id == ticket.ServiceTypeId, cancellationToken);
        var signedAt = command.SignedAt is { } at && at <= clock.UtcNow.AddMinutes(5) ? at : clock.UtcNow;
        var result = ticket.Sign(type, tenant.Tenant.Settings.MinTicketPhotos, command.SignerName, command.SignerPosition, signature.Id, signedAt, command.Location);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var facts = await documentFacts.TicketAsync(ticket, cancellationToken);
        var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.ServiceTicket, ticket.Id, ticket.Folio, facts), AutomationTrigger.Close, cancellationToken);
        if (checks.IsFailure)
        {
            return checks.Error;
        }

        signature.AttachTo(nameof(ServiceTicket), ticket.Id);
        return await closing.CompleteAsync(ticket, facts, cancellationToken);
    }
}

/// <summary>What happens once a ticket is closed: branded PDF, audit entry, notifications and its validation workflow.</summary>
internal sealed class TicketClosing(
    IApplicationDbContext db,
    IFileService files,
    TicketPdfService pdf,
    INotificationPublisher notifications,
    IAuditLog audit,
    IWorkflowEngine workflows)
{
    public async Task<Result<TicketDetailDto>> CompleteAsync(ServiceTicket ticket, IReadOnlyDictionary<string, object?> facts, CancellationToken cancellationToken)
    {
        var document = await pdf.GenerateAsync(ticket, cancellationToken);
        if (document.IsSuccess)
        {
            ticket.AttachPdf(document.Value.Id);
        }

        audit.Record(AuditAction.Signed, nameof(ServiceTicket), ticket.Id, ticket.Folio, details: new { ticket.SignerName, ticket.Total });
        await notifications.PublishAsync(new NotificationMessage(
            NotificationEvent.TicketSigned,
            NotificationSeverity.Success,
            $"Boleta {ticket.Folio} firmada",
            $"La boleta {ticket.Folio} del {ticket.ServiceDate:dd/MM/yyyy} se cerró por {ticket.Total:N2} {ticket.Currency}" +
            (ticket.SignerName is null ? "." : $" con la firma de {ticket.SignerName}."),
            $"boletas/{ticket.Id}",
            CustomerId: ticket.CustomerId,
            AttachmentFileId: ticket.PdfFileId), cancellationToken);

        // Validation workflow of the company (if any applies): the ticket is validated when its last step approves.
        await workflows.StartAsync(new WorkflowSubject(AutomationDocument.ServiceTicket, ticket.Id, ticket.Folio, facts), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        return await TicketDetails.BuildAsync(db, files, ticket, cancellationToken);
    }
}

/// <summary>Closes a ticket that does not require the customer's signature.</summary>
public sealed record CloseTicketCommand(Guid TicketId) : ICommand<TicketDetailDto>;

internal sealed class CloseTicketCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    ITenantClock clock,
    TicketClosing closing,
    DocumentFacts documentFacts,
    IBusinessRules rules) : ICommandHandler<CloseTicketCommand, TicketDetailDto>
{
    public async Task<Result<TicketDetailDto>> Handle(CloseTicketCommand command, CancellationToken cancellationToken)
    {
        var ticket = await TicketLoader.LoadAsync(db, tenant, command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        var type = await db.ServiceTypes.AsNoTracking().FirstAsync(t => t.Id == ticket.ServiceTypeId, cancellationToken);
        var settings = tenant.Tenant.Settings;
        var result = ticket.CloseWithoutSignature(type, settings.MinTicketPhotos, settings.RequireCustomerSignature, clock.UtcNow);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var facts = await documentFacts.TicketAsync(ticket, cancellationToken);
        var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.ServiceTicket, ticket.Id, ticket.Folio, facts), AutomationTrigger.Close, cancellationToken);
        return checks.IsFailure ? checks.Error : await closing.CompleteAsync(ticket, facts, cancellationToken);
    }
}

// ---- Approve / cancel / reopen --------------------------------------------------------------------------

public sealed record ApproveTicketsCommand(IReadOnlyList<Guid> TicketIds) : ICommand<int>;

internal sealed class ApproveTicketsCommandHandler(
    IApplicationDbContext db,
    IUserContext user,
    ITenantClock clock,
    IAuditLog audit,
    IWorkflowEngine workflows,
    DocumentFacts documentFacts,
    IBusinessRules rules) : ICommandHandler<ApproveTicketsCommand, int>
{
    public async Task<Result<int>> Handle(ApproveTicketsCommand command, CancellationToken cancellationToken)
    {
        var ids = command.TicketIds.Distinct().ToList();
        var tickets = await db.Tickets.Include(t => t.Lines).Include(t => t.Photos).Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);
        if (tickets.Count != ids.Count)
        {
            return TicketErrors.NotFound;
        }

        // Tickets under a validation workflow are validated by its approvers.
        var workflowStatuses = await workflows.LatestStatusesAsync(AutomationDocument.ServiceTicket, ids, cancellationToken);
        foreach (var ticket in tickets)
        {
            switch (workflowStatuses.GetValueOrDefault(ticket.Id, WorkflowStatus.Cancelled))
            {
                case WorkflowStatus.Pending:
                    var pending = await workflows.PendingAsync(AutomationDocument.ServiceTicket, ticket.Id, cancellationToken);
                    return WorkflowErrors.InProgress(ticket.Folio, pending?.Step?.Name ?? string.Empty);
                case WorkflowStatus.Rejected when ticket.Status == TicketStatus.Signed:
                    return WorkflowErrors.Rejected(ticket.Folio);
            }

            var checks = await rules.EnforceAsync(new RuleSubject(AutomationDocument.ServiceTicket, ticket.Id, ticket.Folio, await documentFacts.TicketAsync(ticket, cancellationToken)),
                AutomationTrigger.Approve, cancellationToken);
            if (checks.IsFailure)
            {
                return Error.Failure(checks.Error.Code, $"{ticket.Folio}: {checks.Error.Description}");
            }

            var result = ticket.Approve(user.DisplayName, clock.UtcNow);
            if (result.IsFailure)
            {
                return Error.Conflict(result.Error.Code, $"{ticket.Folio}: {result.Error.Description}");
            }

            audit.Record(AuditAction.StatusChanged, nameof(ServiceTicket), ticket.Id, $"{ticket.Folio} → validada");
        }

        await db.SaveChangesAsync(cancellationToken);
        return tickets.Count;
    }
}

public sealed record CancelTicketCommand(Guid TicketId, string Reason) : ICommand;

internal sealed class CancelTicketCommandHandler(IApplicationDbContext db, ITenantClock clock, IAuditLog audit, INotificationPublisher notifications, IWorkflowEngine workflows)
    : ICommandHandler<CancelTicketCommand>
{
    public async Task<Result> Handle(CancelTicketCommand command, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        var wasSigned = ticket.Status is TicketStatus.Signed or TicketStatus.Approved;
        var result = ticket.Cancel(command.Reason, clock.UtcNow);
        if (result.IsFailure)
        {
            return result;
        }

        audit.Record(AuditAction.Cancelled, nameof(ServiceTicket), ticket.Id, ticket.Folio, command.Reason);
        await workflows.CancelPendingAsync(AutomationDocument.ServiceTicket, ticket.Id, "La boleta se canceló.", cancellationToken);
        if (wasSigned)
        {
            await notifications.PublishAsync(new NotificationMessage(
                NotificationEvent.TicketCancelled,
                NotificationSeverity.Warning,
                $"Boleta {ticket.Folio} cancelada",
                $"Motivo: {command.Reason}",
                $"boletas/{ticket.Id}",
                CustomerId: ticket.CustomerId), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record ReopenTicketCommand(Guid TicketId, string Reason) : ICommand;

internal sealed class ReopenTicketCommandHandler(IApplicationDbContext db, IAuditLog audit, IWorkflowEngine workflows) : ICommandHandler<ReopenTicketCommand>
{
    public async Task<Result> Handle(ReopenTicketCommand command, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        var result = ticket.Reopen(command.Reason);
        if (result.IsFailure)
        {
            return result;
        }

        audit.Record(AuditAction.Reopened, nameof(ServiceTicket), ticket.Id, ticket.Folio, command.Reason);
        await workflows.CancelPendingAsync(AutomationDocument.ServiceTicket, ticket.Id, "La boleta se reabrió para corregirla.", cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DeleteDraftTicketCommand(Guid TicketId) : ICommand;

internal sealed class DeleteDraftTicketCommandHandler(IApplicationDbContext db, ITenantContext tenant, IAuditLog audit) : ICommandHandler<DeleteDraftTicketCommand>
{
    public async Task<Result> Handle(DeleteDraftTicketCommand command, CancellationToken cancellationToken)
    {
        var ticket = await db.Tickets.Visible(tenant).Include(t => t.Lines).Include(t => t.Photos).FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken);
        if (ticket is null)
        {
            return TicketErrors.NotFound;
        }

        if (!ticket.IsEditable)
        {
            return Error.Conflict("Tickets.NotDraft", "Solo se eliminan borradores; una boleta cerrada se cancela.");
        }

        db.Tickets.Remove(ticket);
        audit.Record(AuditAction.Deleted, nameof(ServiceTicket), ticket.Id, ticket.Folio);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class TicketLoader
{
    public static Task<ServiceTicket?> LoadAsync(IApplicationDbContext db, ITenantContext tenant, Guid id, CancellationToken cancellationToken) =>
        db.Tickets.Visible(tenant).Include(t => t.Lines).Include(t => t.Photos).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
}
