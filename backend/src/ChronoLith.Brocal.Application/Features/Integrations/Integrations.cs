using System.Globalization;
using System.Text;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Application.Common;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Common;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Integrations;

// ---- API keys -------------------------------------------------------------------------------------------

public sealed record ApiKeyDto(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, DateOnly? ExpiresOn, DateTimeOffset? LastUsedAt, bool IsRevoked, DateTimeOffset CreatedAt, string? CreatedBy);

public sealed record CreatedApiKeyDto(ApiKeyDto Key, string PlainValue);

public sealed record GetApiKeysQuery : IQuery<IReadOnlyList<ApiKeyDto>>;

internal sealed class GetApiKeysQueryHandler(IApplicationDbContext db) : IQueryHandler<GetApiKeysQuery, IReadOnlyList<ApiKeyDto>>
{
    public async Task<Result<IReadOnlyList<ApiKeyDto>>> Handle(GetApiKeysQuery query, CancellationToken cancellationToken) =>
        await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyDto(k.Id, k.Name, k.Prefix, k.Scopes, k.ExpiresOn, k.LastUsedAt, k.IsRevoked, k.CreatedAt, k.CreatedBy))
            .ToListAsync(cancellationToken);
}

public sealed record CreateApiKeyCommand(string Name, IReadOnlyList<string> Scopes, DateOnly? ExpiresOn) : ICommand<CreatedApiKeyDto>;

internal sealed class CreateApiKeyCommandHandler(IApplicationDbContext db, IQuotaGuard quotas, IAuditLog audit) : ICommandHandler<CreateApiKeyCommand, CreatedApiKeyDto>
{
    public async Task<Result<CreatedApiKeyDto>> Handle(CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        var quota = await quotas.EnsureCanAddApiKeyAsync(cancellationToken);
        if (quota.IsFailure)
        {
            return quota.Error;
        }

        var generated = ApiKey.Generate(command.Name, command.Scopes, command.ExpiresOn);
        if (generated.IsFailure)
        {
            return generated.Error;
        }

        var (key, plain) = generated.Value;
        db.ApiKeys.Add(key);
        audit.Record(AuditAction.Created, nameof(ApiKey), key.Id, $"{key.Name} ({key.Prefix}…)");
        await db.SaveChangesAsync(cancellationToken);
        return new CreatedApiKeyDto(new ApiKeyDto(key.Id, key.Name, key.Prefix, key.Scopes, key.ExpiresOn, null, false, key.CreatedAt, key.CreatedBy), plain);
    }
}

public sealed record RevokeApiKeyCommand(Guid Id) : ICommand;

internal sealed class RevokeApiKeyCommandHandler(IApplicationDbContext db, IAuditLog audit) : ICommandHandler<RevokeApiKeyCommand>
{
    public async Task<Result> Handle(RevokeApiKeyCommand command, CancellationToken cancellationToken)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == command.Id, cancellationToken);
        if (key is null)
        {
            return IntegrationErrors.KeyNotFound;
        }

        key.Revoke();
        audit.Record(AuditAction.StatusChanged, nameof(ApiKey), key.Id, $"{key.Name} revocada");
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>Resolves the company of an API key with the required scope (machine-to-machine calls, no Keycloak session).</summary>
public interface IApiKeyAuthenticator
{
    Task<Result<TenantSnapshot>> AuthenticateAsync(string? plainKey, string scope, CancellationToken cancellationToken);
}

internal sealed class ApiKeyAuthenticator(IApplicationDbContext db, TimeProvider timeProvider) : IApiKeyAuthenticator
{
    public async Task<Result<TenantSnapshot>> AuthenticateAsync(string? plainKey, string scope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(plainKey) || !plainKey.StartsWith(ApiKey.KeyPrefix, StringComparison.Ordinal))
        {
            return IntegrationErrors.InvalidApiKey;
        }

        var hash = ApiKey.Hash(plainKey.Trim());
        var key = await db.ApiKeys.IgnoreQueryFilters().FirstOrDefaultAsync(k => k.KeyHash == hash, cancellationToken);
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        if (key is null || !key.IsUsable(today) || !key.HasScope(scope))
        {
            return IntegrationErrors.InvalidApiKey;
        }

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == key.TenantId, cancellationToken);
        if (tenant is null || !tenant.IsOperational(today) || !tenant.IsModuleEnabled(BrocalModules.Integrations))
        {
            return IntegrationErrors.InvalidApiKey;
        }

        key.MarkUsed(timeProvider.GetUtcNow());
        return TenantSnapshot.From(tenant);
    }
}

// ---- Excel exports ("exportación a Excel de todo") ------------------------------------------------------

public sealed record ExportDatasetDto(string Key, string Name, string Module);

public sealed record GetExportDatasetsQuery : IQuery<IReadOnlyList<ExportDatasetDto>>;

internal sealed class GetExportDatasetsQueryHandler(ITenantContext tenant) : IQueryHandler<GetExportDatasetsQuery, IReadOnlyList<ExportDatasetDto>>
{
    public Task<Result<IReadOnlyList<ExportDatasetDto>>> Handle(GetExportDatasetsQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success<IReadOnlyList<ExportDatasetDto>>(
            ExportDatasets.All.Where(d => tenant.IsModuleEnabled(d.Module)).ToList()));
}

internal static class ExportDatasets
{
    public static readonly IReadOnlyList<ExportDatasetDto> All =
    [
        new("tickets", "Boletas de servicio", BrocalModules.Tickets),
        new("ticket-lines", "Conceptos de boletas", BrocalModules.Tickets),
        new("work-orders", "Órdenes de trabajo", BrocalModules.Dispatch),
        new("customers", "Clientes", BrocalModules.Catalogs),
        new("sites", "Locaciones y pozos", BrocalModules.Catalogs),
        new("services", "Servicios y claves SAT", BrocalModules.Pricing),
        new("units", "Unidades y equipos", BrocalModules.Fleet),
        new("fuel", "Cargas de combustible", BrocalModules.Fuel),
        new("maintenance", "Órdenes de taller", BrocalModules.Maintenance),
        new("stock", "Existencias de almacén", BrocalModules.Inventory),
        new("employees", "Personal", BrocalModules.Personnel),
        new("documents", "Permisos y documentos con vencimiento", BrocalModules.Personnel),
        new("attendance", "Asistencia", BrocalModules.Crews),
        new("expenses", "Viáticos", BrocalModules.Expenses),
        new("incidents", "Incidentes HSE", BrocalModules.Hse),
        new("invoices", "Facturas", BrocalModules.Invoicing),
        new("payments", "Cobros", BrocalModules.Collections)
    ];
}

public sealed record ExportDatasetQuery(string Dataset, DateOnly? From, DateOnly? To) : IQuery<FileContent>;

internal sealed class ExportDatasetQueryHandler(IApplicationDbContext db, ITenantContext tenant, ITenantClock clock, IExcelWriter excel, IAuditLog audit)
    : IQueryHandler<ExportDatasetQuery, FileContent>
{
    public async Task<Result<FileContent>> Handle(ExportDatasetQuery query, CancellationToken cancellationToken)
    {
        var dataset = ExportDatasets.All.FirstOrDefault(d => d.Key == query.Dataset);
        if (dataset is null || !tenant.IsModuleEnabled(dataset.Module))
        {
            return IntegrationErrors.UnknownDataset;
        }

        var to = query.To ?? clock.Today;
        var from = query.From ?? to.AddMonths(-3);
        var fromInstant = clock.StartOfDay(from);
        var toInstant = clock.StartOfDay(to.AddDays(1));
        var sheet = query.Dataset switch
        {
            "tickets" => await TicketsAsync(from, to, cancellationToken),
            "ticket-lines" => await TicketLinesAsync(from, to, cancellationToken),
            "work-orders" => await Sheet(db.WorkOrders.AsNoTracking().Where(w => w.ScheduledStart >= fromInstant && w.ScheduledStart < toInstant).OrderBy(w => w.FolioNumber),
                ["Folio", "Estatus", "Prioridad", "Inicio", "Fin", "Descripción", "Referencia cliente"],
                w => new object?[] { w.Folio, w.Status.ToString(), w.Priority.ToString(), w.ScheduledStart, w.ScheduledEnd, w.Description, w.CustomerReference }, cancellationToken),
            "customers" => await Sheet(db.Customers.AsNoTracking().OrderBy(c => c.Name),
                ["Clave", "Nombre", "Razón social", "RFC", "Régimen", "C.P.", "Uso CFDI", "Método de pago", "Días de crédito", "Activo"],
                c => new object?[] { c.Code, c.Name, c.LegalName, c.Rfc, c.TaxRegime, c.PostalCode, c.CfdiUse, c.PaymentMethod, c.CreditDays, c.IsActive }, cancellationToken),
            "sites" => await Sheet(db.Sites.AsNoTracking().OrderBy(s => s.Code),
                ["Clave", "Nombre", "Tipo", "Campo", "Municipio", "Estado", "C.P.", "Latitud", "Longitud", "Activo"],
                s => new object?[] { s.Code, s.Name, s.Type.ToString(), s.FieldName, s.Municipality, s.State, s.PostalCode, s.Latitude, s.Longitude, s.IsActive }, cancellationToken),
            "services" => await Sheet(db.Services.AsNoTracking().OrderBy(s => s.Code),
                ["Clave", "Nombre", "Unidad", "Clave SAT", "Unidad SAT", "IVA", "Precio base", "Activo"],
                s => new object?[] { s.Code, s.Name, s.UnitName, s.SatProductCode, s.SatUnitCode, s.VatRate, s.BasePrice, s.IsActive }, cancellationToken),
            "units" => await Sheet(db.Units.AsNoTracking().OrderBy(u => u.Code),
                ["Número económico", "Descripción", "Tipo", "Estatus", "Marca", "Modelo", "Año", "Placas", "Km", "Horas", "Config. vehicular", "Permiso SCT", "Póliza RC"],
                u => new object?[] { u.Code, u.Name, u.Type.ToString(), u.Status.ToString(), u.Brand, u.Model, u.Year, u.Plates, u.CurrentOdometer, u.CurrentHours, u.VehicleConfig, u.SctPermitNumber, u.InsurancePolicy },
                cancellationToken),
            "fuel" => await Sheet(db.FuelLoads.AsNoTracking().Where(f => f.LoadedAt >= fromInstant && f.LoadedAt < toInstant).OrderBy(f => f.LoadedAt),
                ["Fecha", "Unidad", "Litros", "Precio", "Importe", "Odómetro", "Horómetro", "Tanque lleno", "Estación", "Rendimiento", "Unidad rend.", "Desviación %", "Anomalía"],
                f => new object?[] { f.LoadedAt, db.Units.Where(u => u.Id == f.UnitId).Select(u => u.Code).FirstOrDefault(), f.Liters, f.PricePerLiter, f.Amount, f.Odometer, f.Hours, f.IsFullTank,
                    f.Station, f.Efficiency, f.EfficiencyUnit, f.DeviationPercent, f.IsAnomaly }, cancellationToken),
            "maintenance" => await Sheet(db.MaintenanceOrders.AsNoTracking().Where(o => o.OpenedAt >= fromInstant && o.OpenedAt < toInstant).OrderBy(o => o.FolioNumber),
                ["Folio", "Unidad", "Tipo", "Estatus", "Apertura", "Cierre", "Falla", "Trabajo realizado", "Refacciones", "Mano de obra", "Externo", "Total"],
                o => new object?[] { o.Folio, db.Units.Where(u => u.Id == o.UnitId).Select(u => u.Code).FirstOrDefault(), o.Type.ToString(), o.Status.ToString(), o.OpenedAt, o.ClosedAt, o.ReportedFailure,
                    o.WorkPerformed, o.PartsCost, o.LaborCost, o.ExternalCost, o.TotalCost }, cancellationToken),
            "stock" => await StockAsync(cancellationToken),
            "employees" => await Sheet(db.Employees.AsNoTracking().OrderBy(e => e.Name),
                ["Número", "Nombre", "Puesto", "Categoría", "CURP", "RFC", "NSS", "Ingreso", "Teléfono", "Licencia", "Vence licencia", "Costo/hora", "Activo"],
                e => new object?[] { e.Code, e.Name, e.Position, e.Category.ToString(), e.Curp, e.Rfc, e.Nss, e.HiredOn, e.Phone, e.LicenseNumber, e.LicenseExpiresOn, e.HourlyCost, e.IsActive },
                cancellationToken),
            "documents" => await DocumentsAsync(cancellationToken),
            "attendance" => await Sheet(db.Attendance.AsNoTracking().Where(a => a.Date >= from && a.Date <= to).OrderBy(a => a.Date),
                ["Fecha", "Empleado", "Estatus", "Entrada", "Salida", "Horas", "Horas extra", "Notas"],
                a => new object?[] { a.Date, db.Employees.Where(e => e.Id == a.EmployeeId).Select(e => e.Name).FirstOrDefault(), a.Status.ToString(), a.CheckInAt, a.CheckOutAt, a.Hours, a.OvertimeHours, a.Notes },
                cancellationToken),
            "expenses" => await Sheet(db.ExpenseReports.AsNoTracking().Where(r => r.PeriodStart >= from && r.PeriodStart <= to).OrderBy(r => r.FolioNumber),
                ["Folio", "Empleado", "Del", "Al", "Motivo", "Anticipo", "Comprobado", "Saldo", "Estatus"],
                r => new object?[] { r.Folio, db.Employees.Where(e => e.Id == r.EmployeeId).Select(e => e.Name).FirstOrDefault(), r.PeriodStart, r.PeriodEnd, r.Purpose, r.AdvanceAmount, r.Total, r.Balance, r.Status.ToString() },
                cancellationToken),
            "incidents" => await Sheet(db.Incidents.AsNoTracking().Where(i => i.OccurredAt >= fromInstant && i.OccurredAt < toInstant).OrderBy(i => i.OccurredAt),
                ["Folio", "Fecha", "Tipo", "Severidad", "Estatus", "Descripción", "Causa raíz", "Días perdidos", "Reportó"],
                i => new object?[] { i.Folio, i.OccurredAt, i.Type.ToString(), i.Severity.ToString(), i.Status.ToString(), i.Description, i.RootCause, i.LostDays, i.ReportedByName }, cancellationToken),
            "invoices" => await Sheet(db.Invoices.AsNoTracking().Where(i => i.IssueDate >= from && i.IssueDate <= to).OrderBy(i => i.IssueDate),
                ["Serie", "Folio", "UUID", "Estatus", "Cliente", "Emisión", "Vencimiento", "Método", "Subtotal", "IVA", "Total", "Pagado", "Saldo", "COPADE"],
                i => new object?[] { i.Series, i.Folio, i.Uuid, i.Status.ToString(), db.Customers.Where(c => c.Id == i.CustomerId).Select(c => c.Name).FirstOrDefault(), i.IssueDate, i.DueOn, i.PaymentMethod,
                    i.Subtotal, i.Vat, i.Total, i.PaidAmount, i.Balance, i.CopadeNumber }, cancellationToken),
            _ => await Sheet(db.Payments.AsNoTracking().Where(p => p.PaidAt >= fromInstant && p.PaidAt < toInstant).OrderBy(p => p.PaidAt),
                ["Fecha", "Cliente", "Importe", "Forma de pago", "Referencia", "Complemento", "UUID complemento"],
                p => new object?[] { p.PaidAt, db.Customers.Where(c => c.Id == p.CustomerId).Select(c => c.Name).FirstOrDefault(), p.Amount, p.PaymentForm, p.Reference, p.ComplementStatus.ToString(), p.ComplementUuid },
                cancellationToken)
        };

        audit.Record(AuditAction.Exported, "Export", null, dataset.Name, details: new { query.Dataset, from, to, rows = sheet.Rows.Count });
        await db.SaveChangesAsync(cancellationToken);
        var named = sheet with { Name = dataset.Name, Title = $"{dataset.Name} · {tenant.Tenant.Name} · {from:dd/MM/yyyy} - {to:dd/MM/yyyy}" };
        return new FileContent($"{query.Dataset}_{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx", FileContent.Xlsx, excel.Write([named]));
    }

    private static async Task<ExcelSheet> Sheet<T>(IQueryable<T> source, IReadOnlyList<string> headers, System.Linq.Expressions.Expression<Func<T, object?[]>> row, CancellationToken cancellationToken)
    {
        var rows = await source.Take(50_000).Select(row).ToListAsync(cancellationToken);
        return new ExcelSheet("Datos", headers, rows);
    }

    private async Task<ExcelSheet> TicketsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var tickets = await db.Tickets.AsNoTracking().Where(t => t.ServiceDate >= from && t.ServiceDate <= to).OrderBy(t => t.FolioNumber).Take(50_000).ToListAsync(cancellationToken);
        var customers = await CatalogNames.LoadAsync<Customer>(db, tickets.Select(t => t.CustomerId), cancellationToken);
        var sites = await CatalogNames.LoadAsync<Site>(db, tickets.Select(t => t.SiteId), cancellationToken);
        var types = await CatalogNames.LoadAsync<ServiceType>(db, tickets.Select(t => t.ServiceTypeId), cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, tickets.SelectMany(t => t.UnitIds), cancellationToken);
        return new ExcelSheet("Datos",
            ["Folio", "Fecha", "Estatus", "Cliente", "Locación", "Tipo de servicio", "Unidades", "Capturó", "Firmó", "Fecha firma", "Subtotal", "IVA", "Total", "Moneda"],
            tickets.Select(t => new object?[]
            {
                t.Folio, t.ServiceDate, t.Status.ToString(), customers.NameOf(t.CustomerId), sites.NameOf(t.SiteId), types.NameOf(t.ServiceTypeId),
                string.Join(", ", t.UnitIds.Select(id => units.CodeOf(id))), t.CapturedByName, t.SignerName, t.SignedAt, t.Subtotal, t.Vat, t.Total, t.Currency
            }).ToList());
    }

    private async Task<ExcelSheet> TicketLinesAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var rows = await (from t in db.Tickets.AsNoTracking()
                          from l in t.Lines
                          where t.ServiceDate >= start && t.ServiceDate <= end && t.Status != TicketStatus.Cancelled
                          orderby t.FolioNumber, l.Order
                          select new object?[]
                          {
                              t.Folio, t.ServiceDate, t.Status.ToString(), db.Services.Where(s => s.Id == l.ServiceId).Select(s => s.Code).FirstOrDefault(), l.Description, l.Quantity,
                              l.UnitName, l.UnitPrice, l.PriceSource.ToString(), l.Amount
                          }).Take(100_000).ToListAsync(cancellationToken);
        return new ExcelSheet("Datos", ["Folio", "Fecha", "Estatus", "Servicio", "Descripción", "Cantidad", "Unidad", "Precio", "Origen del precio", "Importe"], rows);
    }

    private async Task<ExcelSheet> StockAsync(CancellationToken cancellationToken)
    {
        var rows = await (from s in db.StockLevels.AsNoTracking()
                          join i in db.InventoryItems.AsNoTracking() on s.ItemId equals i.Id
                          join w in db.Warehouses.AsNoTracking() on s.WarehouseId equals w.Id
                          orderby i.Name
                          select new object?[] { i.Code, i.Name, i.Category.ToString(), w.Name, s.Quantity, i.UnitName, i.AverageCost, s.Quantity * i.AverageCost, i.MinStock })
            .ToListAsync(cancellationToken);
        return new ExcelSheet("Datos", ["Clave", "Artículo", "Categoría", "Almacén", "Existencia", "Unidad", "Costo promedio", "Valor", "Mínimo"], rows);
    }

    private async Task<ExcelSheet> DocumentsAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var documents = await db.ComplianceDocuments.AsNoTracking().OrderBy(d => d.ExpiresOn).ToListAsync(cancellationToken);
        var types = await CatalogNames.LoadAsync<Domain.Compliance.DocumentType>(db, documents.Select(d => d.DocumentTypeId), cancellationToken);
        var units = await CatalogNames.LoadAsync<Unit>(db, documents.Select(d => d.SubjectId), cancellationToken);
        var people = await CatalogNames.LoadAsync<Employee>(db, documents.Select(d => d.SubjectId), cancellationToken);
        return new ExcelSheet("Datos", ["Aplica a", "Clave", "Nombre", "Documento", "Número", "Emisor", "Emisión", "Vencimiento", "Días restantes", "Estatus"],
            documents.Select(d =>
            {
                var names = d.Subject == Domain.Compliance.DocumentSubject.Unit ? units : people;
                return new object?[]
                {
                    d.Subject == Domain.Compliance.DocumentSubject.Unit ? "Unidad" : "Personal", names.CodeOf(d.SubjectId), names.NameOf(d.SubjectId), types.NameOf(d.DocumentTypeId),
                    d.Number, d.Issuer, d.IssuedOn, d.ExpiresOn, ComplianceRules.DaysLeft(d.ExpiresOn, today), d.StatusOn(today, tenant.Tenant.Settings.ExpiryWarningDays).ToString()
                };
            }).ToList());
    }
}

// ---- Accounting (CONTPAQi / Aspel) ----------------------------------------------------------------------

public sealed record AccountingSettingsDto(string CustomersAccount, string RevenueAccount, string VatTransferredAccount, string VatPendingAccount, string BankAccount, AccountingExportFormat Format);

public sealed record GetAccountingSettingsQuery : IQuery<AccountingSettingsDto>;

internal sealed class GetAccountingSettingsQueryHandler(IApplicationDbContext db) : IQueryHandler<GetAccountingSettingsQuery, AccountingSettingsDto>
{
    public async Task<Result<AccountingSettingsDto>> Handle(GetAccountingSettingsQuery query, CancellationToken cancellationToken)
    {
        var s = await db.AccountingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? AccountingSettings.CreateDefault();
        return new AccountingSettingsDto(s.CustomersAccount, s.RevenueAccount, s.VatTransferredAccount, s.VatPendingAccount, s.BankAccount, s.Format);
    }
}

public sealed record SaveAccountingSettingsCommand(string CustomersAccount, string RevenueAccount, string VatTransferredAccount, string VatPendingAccount, string BankAccount,
    AccountingExportFormat Format) : ICommand;

internal sealed class SaveAccountingSettingsCommandHandler(IApplicationDbContext db) : ICommandHandler<SaveAccountingSettingsCommand>
{
    public async Task<Result> Handle(SaveAccountingSettingsCommand command, CancellationToken cancellationToken)
    {
        var settings = await db.AccountingSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = AccountingSettings.CreateDefault();
            db.AccountingSettings.Add(settings);
        }

        var result = settings.Update(command.CustomersAccount, command.RevenueAccount, command.VatTransferredAccount, command.VatPendingAccount, command.BankAccount, command.Format);
        if (result.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

/// <summary>
/// Pólizas contables for the accounting system: one income voucher per stamped invoice (customers / revenue / VAT) and one
/// per payment (bank / customers, VAT pending → transferred). The workbook uses one row per movement, the columns
/// that the import templates of CONTPAQi Contabilidad and Aspel COI expect (adjust the account mapping in Integraciones).
/// </summary>
public sealed record ExportAccountingQuery(DateOnly From, DateOnly To) : IQuery<FileContent>;

internal sealed class ExportAccountingQueryHandler(IApplicationDbContext db, ITenantClock clock, IExcelWriter excel, IAuditLog audit) : IQueryHandler<ExportAccountingQuery, FileContent>
{
    private static readonly string[] Headers = ["Póliza", "Tipo", "Fecha", "Concepto", "Cuenta", "Referencia", "Cargo", "Abono", "UUID"];

    public async Task<Result<FileContent>> Handle(ExportAccountingQuery query, CancellationToken cancellationToken)
    {
        var settings = await db.AccountingSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? AccountingSettings.CreateDefault();
        var invoices = await db.Invoices.AsNoTracking()
            .Where(i => i.Status != InvoiceStatus.Draft && i.Type == CfdiType.Income && i.IssueDate >= query.From && i.IssueDate <= query.To)
            .OrderBy(i => i.IssueDate).ThenBy(i => i.Folio)
            .ToListAsync(cancellationToken);
        var fromInstant = clock.StartOfDay(query.From);
        var toInstant = clock.StartOfDay(query.To.AddDays(1));
        var payments = await db.Payments.AsNoTracking().Include(p => p.Applications).Where(p => p.PaidAt >= fromInstant && p.PaidAt < toInstant).OrderBy(p => p.PaidAt).ToListAsync(cancellationToken);
        var appliedInvoiceIds = payments.SelectMany(p => p.Applications).Select(a => a.InvoiceId).Distinct().ToList();
        var invoiceById = await db.Invoices.AsNoTracking().Where(i => appliedInvoiceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, cancellationToken);

        var rows = new List<object?[]>();
        var number = 0;
        foreach (var invoice in invoices)
        {
            number++;
            var reference = $"{invoice.Series}-{invoice.Folio}";
            var concept = $"Factura {reference} {invoice.Receiver.Name}";
            var vatAccount = invoice.PaymentMethod == "PPD" ? settings.VatPendingAccount : settings.VatTransferredAccount;
            rows.Add([number, "Ingresos", invoice.IssueDate, concept, settings.CustomersAccount, reference, invoice.Total, 0m, invoice.Uuid]);
            rows.Add([number, "Ingresos", invoice.IssueDate, concept, settings.RevenueAccount, reference, 0m, invoice.Subtotal - invoice.Discount, invoice.Uuid]);
            rows.Add([number, "Ingresos", invoice.IssueDate, concept, vatAccount, reference, 0m, invoice.Vat, invoice.Uuid]);
            if (invoice.Status == InvoiceStatus.Cancelled)
            {
                number++;
                var cancelled = DateOnly.FromDateTime(invoice.CancelledAt?.UtcDateTime ?? DateTime.UtcNow);
                rows.Add([number, "Diario", cancelled, $"Cancelación {reference}", settings.CustomersAccount, reference, 0m, invoice.Total, invoice.Uuid]);
                rows.Add([number, "Diario", cancelled, $"Cancelación {reference}", settings.RevenueAccount, reference, invoice.Subtotal - invoice.Discount, 0m, invoice.Uuid]);
                rows.Add([number, "Diario", cancelled, $"Cancelación {reference}", vatAccount, reference, invoice.Vat, 0m, invoice.Uuid]);
            }
        }

        foreach (var payment in payments)
        {
            number++;
            var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(payment.PaidAt, clock.TimeZone).DateTime);
            var concept = $"Cobro {payment.Reference}".Trim();
            rows.Add([number, "Ingresos", date, concept, settings.BankAccount, payment.Reference, payment.Amount, 0m, payment.ComplementUuid]);
            foreach (var application in payment.Applications)
            {
                if (!invoiceById.TryGetValue(application.InvoiceId, out var invoice))
                {
                    continue;
                }

                var reference = $"{invoice.Series}-{invoice.Folio}";
                rows.Add([number, "Ingresos", date, concept, settings.CustomersAccount, reference, 0m, application.Amount, invoice.Uuid]);
                if (invoice.PaymentMethod == "PPD" && invoice.Total > 0)
                {
                    var vat = Money.Round(invoice.Vat * application.Amount / invoice.Total);
                    rows.Add([number, "Ingresos", date, $"Traspaso IVA cobrado {reference}", settings.VatPendingAccount, reference, vat, 0m, invoice.Uuid]);
                    rows.Add([number, "Ingresos", date, $"Traspaso IVA cobrado {reference}", settings.VatTransferredAccount, reference, 0m, vat, invoice.Uuid]);
                }
            }
        }

        audit.Record(AuditAction.Exported, "Accounting", null, $"Pólizas {query.From:dd/MM/yyyy} - {query.To:dd/MM/yyyy}", details: new { invoices = invoices.Count, payments = payments.Count });
        await db.SaveChangesAsync(cancellationToken);

        var name = $"Polizas_{query.From:yyyyMMdd}-{query.To:yyyyMMdd}";
        if (settings.Format == AccountingExportFormat.Csv)
        {
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", Headers));
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(",", row.Select(v => v switch
                {
                    null => string.Empty,
                    DateOnly d => d.ToString("yyyy-MM-dd"),
                    decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
                    _ => $"\"{v.ToString()!.Replace("\"", "\"\"")}\""
                })));
            }

            return new FileContent($"{name}.csv", FileContent.Csv, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
        }

        return new FileContent($"{name}.xlsx", FileContent.Xlsx, excel.Write([new ExcelSheet("Pólizas", Headers, rows, $"Pólizas contables {query.From:dd/MM/yyyy} - {query.To:dd/MM/yyyy}")]));
    }
}
