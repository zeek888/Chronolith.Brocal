using System.Security.Cryptography;
using System.Text;
using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Features.Platform;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using ChronoLith.Brocal.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ChronoLith.Brocal.Infrastructure.Files;

public sealed class FileStorageOptions
{
    public const string SectionName = "Files";

    /// <summary>Root folder of the uploaded and generated files (a Docker volume in containers).</summary>
    public string RootPath { get; init; } = "data/files";

    /// <summary>HMAC key of the download links (32+ characters). Every API instance must share it.</summary>
    public string SigningKey { get; init; } = string.Empty;

    public int DefaultUrlLifetimeMinutes { get; init; } = 60;
}

/// <summary>Files on the local disk under <c>{root}/{tenant}/{yyyy}/{MM}/{id}</c>.</summary>
internal sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private string Root => Path.GetFullPath(options.Value.RootPath);

    public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Stream? stream = File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string key)
    {
        var path = Path.GetFullPath(Path.Combine(Root, key));
        if (!path.StartsWith(Root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ruta de archivo inválida.");
        }

        return path;
    }
}

/// <summary>Signs and verifies download links: the signature binds file, company and expiry.</summary>
public sealed class FileUrlSigner(IOptions<FileStorageOptions> options)
{
    private byte[] Key => Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(options.Value.SigningKey)
        ? throw new InvalidOperationException("Configura Files:SigningKey (32 o más caracteres).")
        : options.Value.SigningKey);

    public string Sign(Guid fileId, Guid tenantId, long expiresAtUnix) =>
        Base64Url(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes($"{fileId:N}:{tenantId:N}:{expiresAtUnix}")));

    public bool Verify(Guid fileId, Guid tenantId, long expiresAtUnix, string signature, DateTimeOffset now) =>
        expiresAtUnix >= now.ToUnixTimeSeconds() &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(fileId, tenantId, expiresAtUnix)), Encoding.ASCII.GetBytes(signature ?? string.Empty));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

internal sealed class FileService(
    BrocalDbContext db,
    IFileStorage storage,
    ITenantContext tenantContext,
    IQuotaGuard quotas,
    FileUrlSigner signer,
    IOptions<FileStorageOptions> options,
    TimeProvider timeProvider) : IFileService
{
    public async Task<Result<StoredFile>> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        FileCategory category,
        CancellationToken cancellationToken,
        string? ownerType = null,
        Guid? ownerId = null)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return await SaveBytesAsync(buffer.ToArray(), fileName, contentType, category, cancellationToken, ownerType, ownerId);
    }

    public async Task<Result<StoredFile>> SaveBytesAsync(
        byte[] content,
        string fileName,
        string contentType,
        FileCategory category,
        CancellationToken cancellationToken,
        string? ownerType = null,
        Guid? ownerId = null)
    {
        var quota = await quotas.EnsureStorageAsync(content.LongLength, cancellationToken);
        if (quota.IsFailure)
        {
            return quota.Error;
        }

        var id = Guid.CreateVersion7();
        var now = timeProvider.GetUtcNow();
        var key = $"{tenantContext.TenantId:N}/{now:yyyy}/{now:MM}/{id:N}";
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        var created = StoredFile.Create(id, fileName, contentType, content.LongLength, hash, key, category);
        if (created.IsFailure)
        {
            return created.Error;
        }

        if (ownerType is not null && ownerId is not null)
        {
            created.Value.AttachTo(ownerType, ownerId.Value);
        }

        using (var stream = new MemoryStream(content, writable: false))
        {
            await storage.SaveAsync(key, stream, cancellationToken);
        }

        db.Files.Add(created.Value);
        return created.Value;
    }

    public async Task<byte[]?> ReadAllBytesAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken)
                   ?? db.Files.Local.FirstOrDefault(f => f.Id == fileId);
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
        return buffer.ToArray();
    }

    public string GetUrl(Guid fileId, TimeSpan? lifetime = null, Guid? tenantId = null)
    {
        var tenant = tenantId ?? tenantContext.TenantId;
        var expires = timeProvider.GetUtcNow().Add(lifetime ?? TimeSpan.FromMinutes(options.Value.DefaultUrlLifetimeMinutes));
        // Rounded to the hour so the browser cache can reuse the same URL for a while.
        var unix = (expires.ToUnixTimeSeconds() / 3600 + 1) * 3600;
        return $"files/{fileId}/content?t={tenant:N}&e={unix}&s={signer.Sign(fileId, tenant, unix)}";
    }

    public async Task<long> UsedBytesAsync(CancellationToken cancellationToken) =>
        await db.Files.SumAsync(f => (long?)f.SizeBytes, cancellationToken) ?? 0;

    public async Task<(string FileName, string ContentType, Stream Content)?> OpenSignedAsync(
        Guid fileId,
        Guid tenantId,
        long expires,
        string signature,
        CancellationToken cancellationToken)
    {
        if (!signer.Verify(fileId, tenantId, expires, signature, timeProvider.GetUtcNow()))
        {
            return null;
        }

        var file = await db.Files.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.TenantId == tenantId, cancellationToken);
        if (file is null)
        {
            return null;
        }

        var stream = await storage.OpenReadAsync(file.StorageKey, cancellationToken);
        return stream is null ? null : (file.FileName, file.ContentType, stream);
    }
}

/// <summary>ASP.NET Data Protection keeps PAC passwords, SMTP passwords and WhatsApp tokens encrypted at rest.</summary>
internal sealed class SecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("ChronoLith.Brocal.Secrets.v1");

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string? Unprotect(string? protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(protectedText);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
