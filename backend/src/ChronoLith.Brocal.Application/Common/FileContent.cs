namespace ChronoLith.Brocal.Application.Common;

/// <summary>A file returned by a query (PDF, Excel, XML, ZIP…).</summary>
public sealed record FileContent(string FileName, string ContentType, byte[] Content)
{
    public const string Pdf = "application/pdf";
    public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string Xml = "application/xml";
    public const string Zip = "application/zip";
    public const string Csv = "text/csv";
}
