using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Automation;
using ChronoLith.Brocal.Domain.Commercial;
using ChronoLith.Brocal.Domain.Compliance;
using ChronoLith.Brocal.Domain.Fiscal;
using ChronoLith.Brocal.Domain.Fleet;
using ChronoLith.Brocal.Domain.Hse;
using ChronoLith.Brocal.Domain.Inventory;
using ChronoLith.Brocal.Domain.Operations;
using ChronoLith.Brocal.Domain.People;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Abstractions.Data;

/// <summary>
/// Unit of work over the Brocal database. Every tenant-owned set is filtered by the current company;
/// EF Core's DbSet already implements the repository pattern, so handlers query aggregates directly.
/// </summary>
public interface IApplicationDbContext
{
    // ---- Platform -------------------------------------------------------------------------------
    DbSet<Tenant> Tenants { get; }

    DbSet<TenantMembership> Memberships { get; }

    DbSet<TenantRole> Roles { get; }

    DbSet<StoredFile> Files { get; }

    DbSet<AuditEntry> AuditEntries { get; }

    DbSet<NotificationRule> NotificationRules { get; }

    DbSet<UserNotification> UserNotifications { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<NotificationChannelSettings> NotificationChannels { get; }

    DbSet<ApiKey> ApiKeys { get; }

    DbSet<AccountingSettings> AccountingSettings { get; }

    DbSet<ScheduledJobRun> ScheduledJobRuns { get; }

    // ---- Workflows and business rules ----------------------------------------------------------
    /// <summary>Company and global definitions (no tenant filter: filter by <c>TenantId == null || TenantId == current</c>).</summary>
    DbSet<WorkflowDefinition> WorkflowDefinitions { get; }

    DbSet<WorkflowInstance> WorkflowInstances { get; }

    /// <summary>Company and global rules (no tenant filter, like <see cref="WorkflowDefinitions"/>).</summary>
    DbSet<BusinessRule> BusinessRules { get; }

    DbSet<AutomationOptOut> AutomationOptOuts { get; }

    // ---- Commercial -----------------------------------------------------------------------------
    DbSet<Customer> Customers { get; }

    DbSet<Contract> Contracts { get; }

    DbSet<Site> Sites { get; }

    DbSet<Service> Services { get; }

    DbSet<PriceList> PriceLists { get; }

    DbSet<ServiceType> ServiceTypes { get; }

    DbSet<Quote> Quotes { get; }

    // ---- Operations -----------------------------------------------------------------------------
    DbSet<ServiceTicket> Tickets { get; }

    DbSet<WorkOrder> WorkOrders { get; }

    // ---- Fleet ----------------------------------------------------------------------------------
    DbSet<Unit> Units { get; }

    DbSet<UnitMeterReading> UnitMeterReadings { get; }

    DbSet<UnitStatusChange> UnitStatusChanges { get; }

    DbSet<UnitPosition> UnitPositions { get; }

    DbSet<MaintenancePlan> MaintenancePlans { get; }

    DbSet<MaintenanceOrder> MaintenanceOrders { get; }

    DbSet<FuelLoad> FuelLoads { get; }

    // ---- Compliance -----------------------------------------------------------------------------
    DbSet<DocumentType> DocumentTypes { get; }

    DbSet<ComplianceDocument> ComplianceDocuments { get; }

    DbSet<ComplianceRequirement> ComplianceRequirements { get; }

    // ---- Inventory ------------------------------------------------------------------------------
    DbSet<Warehouse> Warehouses { get; }

    DbSet<InventoryItem> InventoryItems { get; }

    DbSet<StockLevel> StockLevels { get; }

    DbSet<InventoryMovement> InventoryMovements { get; }

    DbSet<ToolAssignment> ToolAssignments { get; }

    // ---- People ---------------------------------------------------------------------------------
    DbSet<Employee> Employees { get; }

    DbSet<Crew> Crews { get; }

    DbSet<AttendanceRecord> Attendance { get; }

    DbSet<ExpenseReport> ExpenseReports { get; }

    // ---- HSE ------------------------------------------------------------------------------------
    DbSet<Incident> Incidents { get; }

    DbSet<InspectionTemplate> InspectionTemplates { get; }

    DbSet<Inspection> Inspections { get; }

    DbSet<SafetyTalk> SafetyTalks { get; }

    // ---- Fiscal ---------------------------------------------------------------------------------
    DbSet<Invoice> Invoices { get; }

    DbSet<Payment> Payments { get; }

    DbSet<Estimate> Estimates { get; }

    DbSet<CollectionNote> CollectionNotes { get; }

    DbSet<Trip> Trips { get; }

    /// <summary>Generic access used by the catalog services shared across entity types.</summary>
    DbSet<TEntity> Set<TEntity>()
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the operation in a database transaction (with the retry strategy). It commits when the result is a
    /// success and rolls back otherwise; folios are taken inside it so a failure does not leave gaps.
    /// </summary>
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
        where TResult : Result;
}

/// <summary>Consecutive numbers per company and document type.</summary>
public interface IFolioService
{
    /// <summary>Takes the next number of the sequence. Call it inside <see cref="IApplicationDbContext.ExecuteInTransactionAsync{TResult}"/>.</summary>
    Task<long> NextAsync(string key, CancellationToken cancellationToken);
}
