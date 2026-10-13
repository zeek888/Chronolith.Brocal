using System.Reflection;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
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
using ChronoLith.Brocal.Infrastructure.DomainEvents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ChronoLith.Brocal.Infrastructure.Persistence;

/// <summary>
/// Single database for every company (shared schema). Every <see cref="ITenantOwned"/> table is filtered by the
/// named query filter <see cref="TenantFilter"/> with the company of the request, and the interceptors stamp and
/// guard the tenant of every write.
/// </summary>
public sealed class BrocalDbContext(
    DbContextOptions<BrocalDbContext> options,
    ITenantContext tenantContext,
    IDomainEventDispatcher domainEventDispatcher) : DbContext(options), IApplicationDbContext
{
    public const string Schema = "brocal";

    /// <summary>Name of the multi-tenant query filter (to ignore it explicitly in platform queries).</summary>
    public const string TenantFilter = "tenant";

    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(BrocalDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Read by the query filters on every query (parameterized per context instance).</summary>
    internal Guid CurrentTenantId => tenantContext.CurrentTenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();

    public DbSet<TenantRole> Roles => Set<TenantRole>();

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();

    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<NotificationChannelSettings> NotificationChannels => Set<NotificationChannelSettings>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<AccountingSettings> AccountingSettings => Set<AccountingSettings>();

    public DbSet<ScheduledJobRun> ScheduledJobRuns => Set<ScheduledJobRun>();

    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();

    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();

    public DbSet<BusinessRule> BusinessRules => Set<BusinessRule>();

    public DbSet<AutomationOptOut> AutomationOptOuts => Set<AutomationOptOut>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<PriceList> PriceLists => Set<PriceList>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<ServiceTicket> Tickets => Set<ServiceTicket>();

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<Unit> Units => Set<Unit>();

    public DbSet<UnitMeterReading> UnitMeterReadings => Set<UnitMeterReading>();

    public DbSet<UnitStatusChange> UnitStatusChanges => Set<UnitStatusChange>();

    public DbSet<UnitPosition> UnitPositions => Set<UnitPosition>();

    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();

    public DbSet<MaintenanceOrder> MaintenanceOrders => Set<MaintenanceOrder>();

    public DbSet<FuelLoad> FuelLoads => Set<FuelLoad>();

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();

    public DbSet<ComplianceDocument> ComplianceDocuments => Set<ComplianceDocument>();

    public DbSet<ComplianceRequirement> ComplianceRequirements => Set<ComplianceRequirement>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<StockLevel> StockLevels => Set<StockLevel>();

    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<ToolAssignment> ToolAssignments => Set<ToolAssignment>();

    public DbSet<Employee> Employees => Set<Employee>();

    public DbSet<Crew> Crews => Set<Crew>();

    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();

    public DbSet<ExpenseReport> ExpenseReports => Set<ExpenseReport>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<InspectionTemplate> InspectionTemplates => Set<InspectionTemplate>();

    public DbSet<Inspection> Inspections => Set<Inspection>();

    public DbSet<SafetyTalk> SafetyTalks => Set<SafetyTalk>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Estimate> Estimates => Set<Estimate>();

    public DbSet<CollectionNote> CollectionNotes => Set<CollectionNote>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<FolioSequence> FolioSequences => Set<FolioSequence>();

    /// <summary>
    /// Domain events are handled before the commit, inside the same unit of work: handlers add their effects
    /// (notifications, history rows) to this context and everything is saved atomically. Handlers never save.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        for (var round = 0; round < 5; round++)
        {
            var events = ChangeTracker.Entries<AggregateRoot>()
                .SelectMany(entry =>
                {
                    var pending = entry.Entity.DomainEvents.ToList();
                    entry.Entity.ClearDomainEvents();
                    return pending;
                })
                .ToList();

            if (events.Count == 0)
            {
                break;
            }

            await domainEventDispatcher.DispatchAsync(events, cancellationToken);
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<CancellationToken, Task<TResult>> operation, CancellationToken cancellationToken = default)
        where TResult : Result
    {
        if (Database.CurrentTransaction is not null)
        {
            return await operation(cancellationToken);
        }

        var strategy = Database.CreateExecutionStrategy();
        var attempt = 0;
        return await strategy.ExecuteAsync(async ct =>
        {
            // A retried attempt starts clean: the operation loads and adds everything it needs again.
            if (attempt++ > 0)
            {
                ChangeTracker.Clear();
            }

            await using var transaction = await Database.BeginTransactionAsync(ct);
            var result = await operation(ct);
            if (result.IsSuccess)
            {
                await transaction.CommitAsync(ct);
            }
            else
            {
                await transaction.RollbackAsync(ct);
                ChangeTracker.Clear();
            }

            return result;
        }, cancellationToken);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);

        // PostgreSQL timestamptz only accepts UTC offsets in Npgsql: values coming from devices and browsers
        // (e.g. -06:00) are normalized on write and in query parameters.
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType("timestamp with time zone").HaveConversion<UtcDateTimeOffsetConverter>();
        configurationBuilder.Properties<DateTimeOffset?>().HaveColumnType("timestamp with time zone").HaveConversion<UtcDateTimeOffsetConverter>();
    }

    private sealed class UtcDateTimeOffsetConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BrocalDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(e => e.BaseType is null && !e.IsOwned()))
        {
            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
            }
        }

        StoreEnumsAsText(modelBuilder);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantOwned
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.HasQueryFilter(TenantFilter, e => e.TenantId == CurrentTenantId);
        entity.HasIndex(e => e.TenantId);
    }

    /// <summary>Enums are stored as their name (readable in SQL, stable when new values are appended).</summary>
    private static void StoreEnumsAsText(ModelBuilder modelBuilder)
    {
        foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            if (!enumType.IsEnum || property.GetValueConverter() is not null)
            {
                continue;
            }

            var converter = (ValueConverter)Activator.CreateInstance(typeof(EnumToStringConverter<>).MakeGenericType(enumType), new object?[] { null })!;
            property.SetValueConverter(converter);
            property.SetMaxLength(60);
        }
    }
}
