using ChronoLith.Brocal.Application.Abstractions;
using ChronoLith.Brocal.Application.Abstractions.Data;
using ChronoLith.Brocal.Application.Abstractions.Messaging;
using ChronoLith.Brocal.Domain.Abstractions;
using ChronoLith.Brocal.Domain.Platform;
using Microsoft.EntityFrameworkCore;

namespace ChronoLith.Brocal.Application.Features.Files;

public sealed record UploadedFileDto(Guid Id, string FileName, string ContentType, long SizeBytes, string Url);

/// <summary>Stores a file of the current company (photo, signature, permit, receipt, logo…).</summary>
public sealed record UploadFileCommand(Stream Content, string FileName, string ContentType, FileCategory Category) : ICommand<UploadedFileDto>;

internal sealed class UploadFileCommandHandler(IApplicationDbContext db, IFileService files) : ICommandHandler<UploadFileCommand, UploadedFileDto>
{
    public async Task<Result<UploadedFileDto>> Handle(UploadFileCommand command, CancellationToken cancellationToken)
    {
        var saved = await files.SaveAsync(command.Content, command.FileName, command.ContentType, command.Category, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        var file = saved.Value;
        return new UploadedFileDto(file.Id, file.FileName, file.ContentType, file.SizeBytes, files.GetUrl(file.Id));
    }
}

/// <summary>Fresh signed URL of a file of the company.</summary>
public sealed record GetFileUrlQuery(Guid FileId) : IQuery<UploadedFileDto>;

internal sealed class GetFileUrlQueryHandler(IApplicationDbContext db, IFileService files) : IQueryHandler<GetFileUrlQuery, UploadedFileDto>
{
    public async Task<Result<UploadedFileDto>> Handle(GetFileUrlQuery query, CancellationToken cancellationToken)
    {
        var file = await db.Files.AsNoTracking().FirstOrDefaultAsync(f => f.Id == query.FileId, cancellationToken);
        return file is null
            ? FileErrors.NotFound
            : new UploadedFileDto(file.Id, file.FileName, file.ContentType, file.SizeBytes, files.GetUrl(file.Id));
    }
}
