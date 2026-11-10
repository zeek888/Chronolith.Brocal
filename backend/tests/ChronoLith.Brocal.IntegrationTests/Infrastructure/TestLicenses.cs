using System.Security.Cryptography;
using ChronoLith.Licensing;

namespace ChronoLith.Brocal.IntegrationTests.Infrastructure;

/// <summary>
/// ChronoLith licenses signed in the tests with their own key: the factories register <see cref="Verifier"/> (it
/// replaces the embedded keys), so the tests can issue valid, expired or limited licenses for "brocal".
/// </summary>
public static class TestLicenses
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static long _sequence;

    public static LicenseVerifier Verifier { get; } =
        new(new Dictionary<string, string> { [LicenseToken.KeyId(Key)] = Key.ExportSubjectPublicKeyInfoPem() });

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>
    /// Each license is issued after the previous one, so loading it always replaces the active one.
    /// <paramref name="modules"/> grants only those modules (products <c>brocal.&lt;module&gt;</c>); null grants all.
    /// </summary>
    public static string Issue(DateOnly expiresAt, IReadOnlyDictionary<string, int>? limits = null, int graceDays = 30, IEnumerable<string>? modules = null)
    {
        var products = new Dictionary<string, ProductGrant>
        {
            ["brocal"] = new() { Limits = limits ?? new Dictionary<string, int>() }
        };
        foreach (var module in modules ?? [])
        {
            products[$"brocal.{module}"] = ProductGrant.Unlimited;
        }

        return LicenseToken.Sign(new LicenseDocument
        {
            Id = Guid.NewGuid(),
            Customer = "Pruebas de integración",
            IssuedAt = DateTimeOffset.UtcNow.AddSeconds(Interlocked.Increment(ref _sequence)),
            ExpiresAt = expiresAt,
            GraceDays = graceDays,
            Products = products
        }, Key);
    }

    /// <summary>Valid for a year, without limits.</summary>
    public static string Valid() => Issue(Today.AddDays(365));
}
