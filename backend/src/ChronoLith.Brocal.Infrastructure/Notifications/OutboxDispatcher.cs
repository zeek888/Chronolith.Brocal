using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChronoLith.Brocal.Infrastructure.Notifications;

/// <summary>
/// Delivers pending e-mail and WhatsApp messages of every company with exponential back-off. A PostgreSQL advisory
/// lock keeps a single dispatcher active when several API instances run.
/// </summary>
internal sealed class OutboxDispatcher(IServiceScopeFactory scopeFactory, TimeProvider timeProvider, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    private const long LockKey = 7_100_421_001;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Error al despachar notificaciones pendientes");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    internal async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BrocalDbContext>();
        if (!await AdvisoryLock.TryAcquireAsync(db, LockKey, cancellationToken))
        {
            return;
        }

        try
        {
            var now = timeProvider.GetUtcNow();
            var pending = await db.OutboxMessages
                .IgnoreQueryFilters()
                .Where(o => o.Status == OutboxStatus.Pending && o.NextAttemptAt <= now)
                .OrderBy(o => o.NextAttemptAt)
                .Take(50)
                .ToListAsync(cancellationToken);

            if (pending.Count == 0)
            {
                return;
            }

            var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            var whatsapp = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();
            var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

            var tenantIds = pending.Select(p => p.TenantId).Distinct().ToList();
            var channels = await db.NotificationChannels.IgnoreQueryFilters().AsNoTracking()
                .Where(c => tenantIds.Contains(c.TenantId))
                .ToDictionaryAsync(c => c.TenantId, cancellationToken);

            foreach (var message in pending)
            {
                channels.TryGetValue(message.TenantId, out var settings);
                try
                {
                    bool delivered;
                    if (message.Channel == OutboxChannel.Email)
                    {
                        var attachment = await LoadAttachmentAsync(db, storage, message, cancellationToken);
                        delivered = await email.SendAsync(new EmailMessage(message.Recipient, message.Subject, message.Body, attachment), SmtpOf(settings, protector), cancellationToken);
                    }
                    else
                    {
                        var title = message.Subject.Contains(" · ") ? message.Subject[(message.Subject.IndexOf(" · ", StringComparison.Ordinal) + 3)..] : message.Subject;
                        delivered = await whatsapp.SendAsync(message.Recipient, title, message.Body, WhatsAppOf(settings, protector), cancellationToken);
                    }

                    message.MarkSent(timeProvider.GetUtcNow(), simulated: !delivered);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning(ex, "No se pudo enviar {Channel} a {Recipient}", message.Channel, message.Recipient);
                    message.MarkFailed(timeProvider.GetUtcNow(), ex.Message);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            await AdvisoryLock.ReleaseAsync(db, LockKey);
        }
    }

    private static SmtpAccount? SmtpOf(NotificationChannelSettings? settings, ISecretProtector protector) =>
        settings is { UseOwnSmtp: true, SmtpHost: not null, FromAddress: not null }
            ? new SmtpAccount(settings.SmtpHost, settings.SmtpPort, settings.SmtpUseSsl, settings.SmtpUser, protector.Unprotect(settings.SmtpPasswordProtected), settings.FromAddress, settings.FromName)
            : null;

    private static WhatsAppAccount? WhatsAppOf(NotificationChannelSettings? settings, ISecretProtector protector) =>
        settings is { WhatsAppEnabled: true, WhatsAppPhoneNumberId: not null } && protector.Unprotect(settings.WhatsAppTokenProtected) is { } token
            ? new WhatsAppAccount(settings.WhatsAppPhoneNumberId, token, settings.WhatsAppTemplate, settings.WhatsAppLanguage)
            : null;

    private static async Task<(string, string, byte[])?> LoadAttachmentAsync(BrocalDbContext db, IFileStorage storage, OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.AttachmentFileId is null)
        {
            return null;
        }

        var file = await db.Files.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == message.AttachmentFileId && f.TenantId == message.TenantId, cancellationToken);
        if (file is null)
        {
            return null;
        }

        await using var stream = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return (file.FileName, file.ContentType, buffer.ToArray());
    }
}

/// <summary>Session-level PostgreSQL advisory locks (one runner of a job across instances).</summary>
internal static class AdvisoryLock
{
    public static async Task<bool> TryAcquireAsync(BrocalDbContext db, long key, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var acquired = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({key}) AS \"Value\"").ToListAsync(cancellationToken);
        if (acquired.FirstOrDefault())
        {
            return true;
        }

        await db.Database.CloseConnectionAsync();
        return false;
    }

    public static async Task ReleaseAsync(BrocalDbContext db, long key)
    {
        try
        {
            await db.Database.SqlQuery<bool>($"SELECT pg_advisory_unlock({key}) AS \"Value\"").ToListAsync();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
