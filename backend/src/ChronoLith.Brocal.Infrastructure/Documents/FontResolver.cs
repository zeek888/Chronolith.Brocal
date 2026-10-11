using PdfSharp.Fonts;

namespace ChronoLith.Brocal.Infrastructure.Documents;

/// <summary>
/// PDFsharp needs the TTF bytes on Linux (no GDI). Every family requested by the documents is mapped to the first
/// sans-serif family found: a configured folder, Liberation Sans, DejaVu Sans (Docker image) or Arial/Segoe (Windows).
/// </summary>
internal sealed class BrocalFontResolver : IFontResolver
{
    private readonly Dictionary<string, byte[]> _faces = new(StringComparer.Ordinal);

    private static readonly (string Regular, string Bold, string Italic, string BoldItalic)[] Candidates =
    [
        ("LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf"),
        ("DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"),
        ("arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        ("Arial.ttf", "Arial Bold.ttf", "Arial Italic.ttf", "Arial Bold Italic.ttf"),
        ("segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        ("NotoSans-Regular.ttf", "NotoSans-Bold.ttf", "NotoSans-Italic.ttf", "NotoSans-BoldItalic.ttf")
    ];

    public BrocalFontResolver(string? configuredFolder)
    {
        var folders = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredFolder))
        {
            folders.Add(configuredFolder);
        }

        folders.AddRange(
        [
            "/usr/share/fonts/truetype/liberation",
            "/usr/share/fonts/truetype/liberation2",
            "/usr/share/fonts/liberation",
            "/usr/share/fonts/truetype/dejavu",
            "/usr/share/fonts/dejavu",
            "/usr/share/fonts/noto",
            "/usr/share/fonts/truetype/noto",
            "/Library/Fonts",
            "/System/Library/Fonts/Supplemental",
            Environment.GetFolderPath(Environment.SpecialFolder.Fonts)
        ]);

        foreach (var (regular, bold, italic, boldItalic) in Candidates)
        {
            foreach (var folder in folders.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)))
            {
                var regularPath = Path.Combine(folder, regular);
                var boldPath = Path.Combine(folder, bold);
                if (!File.Exists(regularPath) || !File.Exists(boldPath))
                {
                    continue;
                }

                _faces["R"] = File.ReadAllBytes(regularPath);
                _faces["B"] = File.ReadAllBytes(boldPath);
                _faces["I"] = File.Exists(Path.Combine(folder, italic)) ? File.ReadAllBytes(Path.Combine(folder, italic)) : _faces["R"];
                _faces["BI"] = File.Exists(Path.Combine(folder, boldItalic)) ? File.ReadAllBytes(Path.Combine(folder, boldItalic)) : _faces["B"];
                FamilyFound = Path.GetFileNameWithoutExtension(regular);
                return;
            }
        }
    }

    public string? FamilyFound { get; }

    public bool IsAvailable => _faces.Count > 0;

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new((isBold, isItalic) switch
        {
            (true, true) => "BI",
            (true, false) => "B",
            (false, true) => "I",
            _ => "R"
        });

    public byte[] GetFont(string faceName) =>
        _faces.TryGetValue(faceName, out var bytes)
            ? bytes
            : throw new InvalidOperationException("No hay fuentes TrueType para generar PDF. Instala fonts-liberation o configura Pdf:FontsPath.");
}
