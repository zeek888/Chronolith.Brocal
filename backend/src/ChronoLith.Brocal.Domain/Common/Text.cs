using System.Text.RegularExpressions;

namespace ChronoLith.Brocal.Domain.Common;

public static partial class HexColor
{
    public static bool IsValid(string? value) => value is not null && Pattern().IsMatch(value);

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Pattern();
}

/// <summary>Business codes (customers, units, services…): upper case, letters, digits and <c>. _ - /</c>.</summary>
public static partial class Codes
{
    public const int MaxLength = 40;

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public static bool IsValid(string? code) => code is not null && Pattern().IsMatch(code);

    [GeneratedRegex("^[A-Z0-9Ñ][A-Z0-9Ñ._/-]{0,39}$")]
    private static partial Regex Pattern();
}

/// <summary>URL-friendly identifier of a tenant (<c>servicios-del-golfo</c>).</summary>
public static partial class Slugs
{
    public static string Normalize(string slug) => slug.Trim().ToLowerInvariant();

    public static bool IsValid(string? slug) => slug is not null && Pattern().IsMatch(slug);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,38}[a-z0-9])$")]
    private static partial Regex Pattern();
}

/// <summary>Mexican tax identifiers (SAT / RENAPO).</summary>
public static partial class MexicanIds
{
    /// <summary>Generic RFC for sales to the general public.</summary>
    public const string GenericRfc = "XAXX010101000";

    /// <summary>Generic RFC for foreign customers.</summary>
    public const string ForeignRfc = "XEXX010101000";

    public static string NormalizeRfc(string rfc) => rfc.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);

    /// <summary>12 characters (legal entity) or 13 (individual), including the SAT generic RFCs.</summary>
    public static bool IsValidRfc(string? rfc) => rfc is not null && RfcPattern().IsMatch(rfc);

    public static bool IsLegalEntityRfc(string rfc) => rfc.Length == 12;

    public static bool IsValidCurp(string? curp) => curp is not null && CurpPattern().IsMatch(curp);

    /// <summary>Five-digit postal code (lugar de expedición, domicilio fiscal).</summary>
    public static bool IsValidPostalCode(string? value) => value is not null && PostalCodePattern().IsMatch(value);

    [GeneratedRegex("^([A-ZÑ&]{3,4})(\\d{2})(0[1-9]|1[0-2])(0[1-9]|[12]\\d|3[01])([A-Z\\d]{2})([A\\d])$")]
    private static partial Regex RfcPattern();

    [GeneratedRegex("^[A-Z][AEIOUX][A-Z]{2}\\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\\d|3[01])[HMX](AS|B[CS]|C[CLMSH]|D[FG]|G[TR]|HG|JC|M[CNS]|N[ETL]|OC|PL|Q[TR]|S[PLR]|T[CSL]|VZ|YN|ZS|NE)[B-DF-HJ-NP-TV-Z]{3}[A-Z\\d]\\d$")]
    private static partial Regex CurpPattern();

    [GeneratedRegex("^\\d{5}$")]
    private static partial Regex PostalCodePattern();
}

public static class Money
{
    /// <summary>Currency amounts are rounded half away from zero to cents (SAT rule for CFDI).</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundRate(decimal amount) => Math.Round(amount, 6, MidpointRounding.AwayFromZero);
}

/// <summary>Location captured in the field (GPS of the device that captured a ticket, a photo or a check-in).</summary>
public sealed record GeoPoint(decimal Latitude, decimal Longitude, decimal? AccuracyMeters = null)
{
    public bool IsValid => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}
