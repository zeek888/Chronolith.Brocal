using System.Data;
using System.Text.Json;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ChronoLith.Brocal.Infrastructure.Persistence;

/// <summary>
/// Atomic upsert on <c>folio_sequences</c>. PostgreSQL locks the row until the transaction ends, so concurrent
/// requests get consecutive numbers and a rollback returns the number.
/// </summary>
internal sealed class FolioService(BrocalDbContext db, ITenantContext tenantContext) : IFolioService
{
    public async Task<long> NextAsync(string key, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
            opened = true;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.CommandText =
                $"""
                INSERT INTO {BrocalDbContext.Schema}.folio_sequences (id, tenant_id, key, next_value)
                VALUES (@id, @tenant, @key, 2)
                ON CONFLICT (tenant_id, key) DO UPDATE SET next_value = {BrocalDbContext.Schema}.folio_sequences.next_value + 1
                RETURNING next_value - 1
                """;

            AddParameter(command, "id", Guid.CreateVersion7());
            AddParameter(command, "tenant", tenantContext.TenantId);
            AddParameter(command, "key", key);

            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (opened)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}

/// <summary>Explicit business actions in the audit log; saved with the unit of work of the caller.</summary>
internal sealed class AuditLog(BrocalDbContext db, IUserContext userContext, ITenantContext tenantContext, TimeProvider timeProvider) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Record(AuditAction action, string entityType, Guid? entityId, string? summary, string? reason = null, object? details = null)
    {
        db.AuditEntries.Add(new AuditEntry(
            tenantContext.HasTenant ? tenantContext.TenantId : null,
            timeProvider.GetUtcNow(),
            userContext.IsAuthenticated ? userContext.IdentityId : null,
            userContext.IsAuthenticated ? userContext.DisplayName : "system",
            action,
            entityType,
            entityId,
            summary,
            details is null ? null : JsonSerializer.Serialize(details, Json),
            reason,
            userContext.CorrelationId,
            userContext.IpAddress));
    }
}
