using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.People;

public sealed record ExpenseItemDto(Guid Id, DateOnly Date, ExpenseCategory Category, string Description, string? Vendor, decimal Amount, bool HasInvoice, string? InvoiceUuid, Guid? ReceiptFileId, string? ReceiptUrl);

public sealed record ExpenseReportDto(
    Guid Id, string Folio, Guid EmployeeId, string? EmployeeName, Guid? WorkOrderId, string? WorkOrderFolio, DateOnly PeriodStart, DateOnly PeriodEnd, string Purpose,
    decimal AdvanceAmount, decimal Total, decimal Balance, ExpenseStatus Status, DateTimeOffset? SubmittedAt, DateTimeOffset? ReviewedAt, string? ReviewedBy,
    string? RejectionReason, DateTimeOffset? PaidAt, IReadOnlyList<ExpenseItemDto> Items, DateTimeOffset CreatedAt);

/// <summary>"Own" scope: field staff see only their reports (linked employee of the membership).</summary>
internal static class ExpenseVisibility
{
    public static IQueryable<ExpenseReport> Visible(this IQueryable<ExpenseReport> reports, ITenantContext tenant) =>
        tenant.HasAny(Permissions.ExpensesRead, Permissions.ExpensesApprove)
            ? reports
            : reports.Where(r => r.EmployeeId == tenant.Membership!.EmployeeId);
}

public sealed record SearchExpenseReportsQuery(ExpenseStatus? Status, Guid? EmployeeId, int Page = 1, int PageSize = 25) : IQuery<PagedResult<ExpenseReportDto>>;

internal sealed class SearchExpenseReportsQueryHandler(IApplicationDbContext db, ITenantContext tenant) : IQueryHandler<SearchExpenseReportsQuery, PagedResult<ExpenseReportDto>>
{
    public async Task<Result<PagedResult<ExpenseReportDto>>> Handle(SearchExpenseReportsQuery query, CancellationToken cancellationToken)
    {
        return await db.ExpenseReports.AsNoTracking().Visible(tenant)
            .WhereIf(query.Status is not null, r => r.Status == query.Status)
            .WhereIf(query.EmployeeId is not null, r => r.EmployeeId == query.EmployeeId)
            .OrderByDescending(r => r.FolioNumber)
            .Select(r => new ExpenseReportDto(r.Id, r.Folio, r.EmployeeId, db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.Name).FirstOrDefault(),
                r.WorkOrderId, db.WorkOrders.Where(w => w.Id == r.WorkOrderId).Select(w => w.Folio).FirstOrDefault(), r.PeriodStart, r.PeriodEnd, r.Purpose,
                r.AdvanceAmount, r.Total, r.Balance, r.Status, r.SubmittedAt, r.ReviewedAt, r.ReviewedBy, r.RejectionReason, r.PaidAt, new List<ExpenseItemDto>(), r.CreatedAt))
            .ToPagedAsync(new PageRequest(null, query.Page, query.PageSize), cancellationToken);
    }
}

public sealed record GetExpenseReportQuery(Guid Id) : IQuery<ExpenseReportDto>;

internal sealed class GetExpenseReportQueryHandler(IApplicationDbContext db, ITenantContext tenant, IFileService files) : IQueryHandler<GetExpenseReportQuery, ExpenseReportDto>
{
    public async Task<Result<ExpenseReportDto>> Handle(GetExpenseReportQuery query, CancellationToken cancellationToken)
    {
        var report = await db.ExpenseReports.AsNoTracking().Visible(tenant).Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == query.Id, cancellationToken);
        return report is null ? ExpenseErrors.NotFound : await ExpenseProjection.ToDtoAsync(db, files, report, cancellationToken);
    }
}

internal static class ExpenseProjection
{
    public static async Task<ExpenseReportDto> ToDtoAsync(IApplicationDbContext db, IFileService files, ExpenseReport r, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().Where(e => e.Id == r.EmployeeId).Select(e => e.Name).FirstOrDefaultAsync(cancellationToken);
        var order = r.WorkOrderId is null ? null : await db.WorkOrders.AsNoTracking().Where(w => w.Id == r.WorkOrderId).Select(w => w.Folio).FirstOrDefaultAsync(cancellationToken);
        return new ExpenseReportDto(r.Id, r.Folio, r.EmployeeId, employee, r.WorkOrderId, order, r.PeriodStart, r.PeriodEnd, r.Purpose, r.AdvanceAmount, r.Total, r.Balance,
            r.Status, r.SubmittedAt, r.ReviewedAt, r.ReviewedBy, r.RejectionReason, r.PaidAt,
            r.Items.OrderBy(i => i.Date).Select(i => new ExpenseItemDto(i.Id, i.Date, i.Category, i.Description, i.Vendor, i.Amount, i.HasInvoice, i.InvoiceUuid,
                i.ReceiptFileId, i.ReceiptFileId is { } file ? files.GetUrl(file) : null)).ToList(), r.CreatedAt);
    }
}

public sealed record SaveExpenseReportCommand(Guid? Id, ExpenseReportData Data) : ICommand<Guid>;

internal sealed class SaveExpenseReportCommandValidator : AbstractValidator<SaveExpenseReportCommand>
{
    public SaveExpenseReportCommandValidator()
    {
        RuleFor(c => c.Data.Purpose).NotEmpty().MaximumLength(500);
        RuleFor(c => c.Data.AdvanceAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.Data.PeriodEnd).GreaterThanOrEqualTo(c => c.Data.PeriodStart);
    }
}

internal sealed class SaveExpenseReportCommandHandler(IApplicationDbContext db, ITenantContext tenant, IFolioService folios) : ICommandHandler<SaveExpenseReportCommand, Guid>
{
    public async Task<Result<Guid>> Handle(SaveExpenseReportCommand command, CancellationToken cancellationToken)
    {
        var data = command.Data;
        var ownOnly = !tenant.HasAny(Permissions.ExpensesRead, Permissions.ExpensesApprove);
        if (ownOnly)
        {
            if (tenant.Membership?.EmployeeId is not { } own)
            {
                return Error.Forbidden("Expenses.NoEmployee", "Tu usuario no está ligado a una ficha de empleado; pide al administrador que la asigne.");
            }

            data = data with { EmployeeId = own };
        }

        if (!await db.Employees.AnyAsync(e => e.Id == data.EmployeeId, cancellationToken))
        {
            return CatalogErrors.ReferenceNotFound(EmployeeErrors.Catalog);
        }

        if (command.Id is { } id)
        {
            var report = await db.ExpenseReports.Visible(tenant).Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
            if (report is null)
            {
                return ExpenseErrors.NotFound;
            }

            var updated = report.Update(data);
            if (updated.IsFailure)
            {
                return updated.Error;
            }

            await db.SaveChangesAsync(cancellationToken);
            return report.Id;
        }

        return await db.ExecuteInTransactionAsync(async ct =>
        {
            var number = await folios.NextAsync(FolioKeys.Expense, ct);
            var created = ExpenseReport.Create(FolioSequence.Format(tenant.Tenant.Settings.ExpensePrefix, number), number, data);
            if (created.IsFailure)
            {
                return Result.Failure<Guid>(created.Error);
            }

            db.ExpenseReports.Add(created.Value);
            await db.SaveChangesAsync(ct);
            return Result.Success(created.Value.Id);
        }, cancellationToken);
    }
}

public enum ExpenseAction
{
    Submit = 0,
    Approve = 1,
    Reject = 2,
    MarkPaid = 3
}

public sealed record ChangeExpenseStatusCommand(Guid Id, ExpenseAction Action, string? Reason) : ICommand;

internal sealed class ChangeExpenseStatusCommandHandler(
    IApplicationDbContext db,
    ITenantContext tenant,
    IUserContext user,
    ITenantClock clock,
    INotificationPublisher notifications) : ICommandHandler<ChangeExpenseStatusCommand>
{
    public async Task<Result> Handle(ChangeExpenseStatusCommand command, CancellationToken cancellationToken)
    {
        var report = await db.ExpenseReports.Visible(tenant).Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == command.Id, cancellationToken);
        if (report is null)
        {
            return ExpenseErrors.NotFound;
        }

        if (command.Action != ExpenseAction.Submit && !tenant.Has(Permissions.ExpensesApprove))
        {
            return Error.Forbidden("Expenses.Forbidden", "No tienes permiso para autorizar comprobaciones.");
        }

        var now = clock.UtcNow;
        var result = command.Action switch
        {
            ExpenseAction.Submit => report.Submit(now),
            ExpenseAction.Approve => report.Approve(user.DisplayName, now),
            ExpenseAction.Reject => report.Reject(user.DisplayName, now, command.Reason ?? string.Empty),
            _ => report.MarkPaid(now)
        };

        if (result.IsFailure)
        {
            return result;
        }

        if (command.Action == ExpenseAction.Submit)
        {
            await notifications.PublishAsync(new NotificationMessage(NotificationEvent.ExpenseSubmitted, NotificationSeverity.Info,
                $"Comprobación {report.Folio} enviada", $"{report.Purpose} · total {report.Total:C2}, anticipo {report.AdvanceAmount:C2}.", $"viaticos/{report.Id}"), cancellationToken);
        }
        else
        {
            var owner = await db.Memberships.AsNoTracking().Where(m => m.EmployeeId == report.EmployeeId).Select(m => m.Id).ToListAsync(cancellationToken);
            await notifications.PublishAsync(new NotificationMessage(NotificationEvent.ExpenseSubmitted, NotificationSeverity.Info,
                $"Comprobación {report.Folio}: {report.Status}", command.Reason ?? $"Saldo: {report.Balance:C2}.", $"viaticos/{report.Id}", owner), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
