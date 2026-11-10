using ChronoLith.Brocal.Domain.Abstractions;

namespace ChronoLith.Brocal.Domain.Platform;

/// <summary>
/// ChronoLith license of the installation (standalone mode): a single global row, outside every company, with the
/// id of this installation (created on the first start; licenses are issued for it) and the token a platform
/// administrator loaded from the "Licencia" page. Every company of the installation shares the license and its limits.
/// </summary>
public sealed class InstallationLicense : Entity
{
    /// <summary>Primary key of the only row: a second insert (two replicas starting together) collides on it.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-000000000001");

    private InstallationLicense()
    {
    }

    private InstallationLicense(Guid instanceId)
        : base(SingletonId)
    {
        InstanceId = instanceId;
    }

    public Guid InstanceId { get; private set; }

    /// <summary>Token loaded from the application (already verified); null while only the configuration provides one.</summary>
    public string? Token { get; private set; }

    public DateTimeOffset? UploadedAt { get; private set; }

    public string? UploadedBy { get; private set; }

    public static InstallationLicense Create(Guid instanceId) => new(instanceId);

    public void Upload(string token, DateTimeOffset at, string? by)
    {
        Token = token;
        UploadedAt = at;
        UploadedBy = by is { Length: > 150 } ? by[..150] : by;
    }
}
