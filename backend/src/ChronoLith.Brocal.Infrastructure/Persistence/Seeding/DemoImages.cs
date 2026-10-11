using System.Buffers.Binary;
using System.IO.Compression;

namespace ChronoLith.Brocal.Infrastructure.Persistence.Seeding;

/// <summary>Tiny PNG encoder used to draw sample signatures for the demo company (no imaging library needed).</summary>
internal static class DemoImages
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>A hand-written-looking scribble, different for each seed.</summary>
    public static byte[] Signature(int seed)
    {
        const int width = 420;
        const int height = 150;
        var pixels = new byte[width * height * 4];
        var random = new Random(seed);

        double x = 20, y = height / 2.0;
        var amplitude = 25 + random.Next(20);
        var frequency = 0.05 + random.NextDouble() * 0.05;
        var phase = random.NextDouble() * Math.PI;

        for (var step = 0; step < 380; step++)
        {
            var nx = 20 + step;
            var ny = height / 2.0 + Math.Sin(step * frequency + phase) * amplitude * Math.Sin(step / 60.0 + 0.6) + Math.Cos(step * 0.17) * 6;
            DrawLine(pixels, width, height, x, y, nx, ny);
            x = nx;
            y = ny;
        }

        // Final underline stroke.
        DrawLine(pixels, width, height, 60, height - 28, 330, height - 34);
        return Encode(pixels, width, height);
    }

    private static void DrawLine(byte[] pixels, int width, int height, double x0, double y0, double x1, double y1)
    {
        var steps = (int)Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 2 + 1;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (double)steps;
            var cx = x0 + (x1 - x0) * t;
            var cy = y0 + (y1 - y0) * t;
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var px = (int)Math.Round(cx) + dx;
                    var py = (int)Math.Round(cy) + dy;
                    if (px < 0 || py < 0 || px >= width || py >= height)
                    {
                        continue;
                    }

                    var index = (py * width + px) * 4;
                    pixels[index] = 20;
                    pixels[index + 1] = 40;
                    pixels[index + 2] = 110;
                    pixels[index + 3] = 255;
                }
            }
        }
    }

    private static byte[] Encode(byte[] rgba, int width, int height)
    {
        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // RGBA
        WriteChunk(output, "IHDR", header);

        using (var raw = new MemoryStream())
        {
            using (var zlib = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var row = 0; row < height; row++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(rgba, row * width * 4, width * 4);
                }
            }

            WriteChunk(output, "IDAT", raw.ToArray());
        }

        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = Crc(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static uint Crc(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
