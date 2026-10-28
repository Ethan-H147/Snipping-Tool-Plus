using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace QuietSnip.Diagnostics;

internal sealed record PixelComparison(bool SameSize, int Width, int Height, long DifferentRgbPixels,
    long DifferentAlphaPixels, int MaxRgbCodeError, int MaxAlphaCodeError, double MeanAbsoluteRgbCodeError,
    Rectangle? DifferenceBounds, List<object> FirstDifferences);

internal static class PixelEvidence
{
    internal static byte[] Pixels(Bitmap bitmap)
    {
        var bytes = new byte[checked(bitmap.Width * bitmap.Height * 4)];
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * bitmap.Width * 4, bitmap.Width * 4);
        }
        finally { bitmap.UnlockBits(data); }
        return bytes;
    }

    internal static PixelComparison Compare(Bitmap expected, Bitmap actual)
    {
        if (expected.Size != actual.Size) return new(false, actual.Width, actual.Height, 0, 0, 0, 0, 0, null, []);
        var a = Pixels(expected); var b = Pixels(actual);
        long changed = 0, alpha = 0, total = 0; int max = 0, maxAlpha = 0;
        int left = expected.Width, top = expected.Height, right = -1, bottom = -1;
        var differences = new List<object>();
        for (int i = 0; i < a.Length; i += 4)
        {
            bool different = false;
            for (int c = 0; c < 3; c++)
            {
                int error = Math.Abs(a[i + c] - b[i + c]);
                max = Math.Max(max, error); total += error; different |= error != 0;
            }
            if (different) changed++;
            int alphaError = Math.Abs(a[i + 3] - b[i + 3]);
            if (alphaError != 0) alpha++;
            maxAlpha = Math.Max(maxAlpha, alphaError);
            if (different || alphaError != 0)
            {
                int x = i / 4 % expected.Width, y = i / 4 / expected.Width;
                left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                if (differences.Count < 16) differences.Add(new { X = x, Y = y,
                    ExpectedRgba = new[] { (int)a[i + 2], a[i + 1], a[i], a[i + 3] },
                    ActualRgba = new[] { (int)b[i + 2], b[i + 1], b[i], b[i + 3] } });
            }
        }
        return new(true, actual.Width, actual.Height, changed, alpha, max, maxAlpha, total / (expected.Width * (double)expected.Height * 3),
            right < 0 ? null : Rectangle.FromLTRB(left, top, right + 1, bottom + 1), differences);
    }

    internal static object Inspect(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        var chunks = PngChunks(bytes);
        using var bitmap = new Bitmap(path);
        return new {
            File = Path.GetFullPath(path), Bytes = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            bitmap.Width, bitmap.Height, PixelFormat = bitmap.PixelFormat.ToString(),
            DecodedBgraSha256 = Convert.ToHexString(SHA256.HashData(Pixels(bitmap))),
            PngChunks = chunks, ColorInterpretation = "Decoded code values only; no manual ICC conversion. Metadata describes this file only."
        };
    }

    internal static List<object> PngChunks(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 20 || !bytes.AsSpan(0, 8).SequenceEqual(signature)) throw new InvalidDataException("Not a PNG.");
        var chunks = new List<object>();
        int offset = 8; bool header = false, end = false, dataFound = false;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 12) throw new InvalidDataException("Truncated PNG chunk.");
            uint length = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > bytes.Length - offset - 12) throw new InvalidDataException("PNG chunk exceeds file length.");
            int size = checked((int)length);
            string type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (!header && (type != "IHDR" || size != 13)) throw new InvalidDataException("Missing PNG IHDR.");
            if (header && type == "IHDR") throw new InvalidDataException("Duplicate PNG IHDR.");
            var payload = bytes.AsSpan(offset + 8, size);
            uint recordedCrc = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 8 + size, 4));
            if (Crc(bytes.AsSpan(offset + 4, size + 4)) != recordedCrc) throw new InvalidDataException("PNG CRC mismatch: " + type);
            object? detail = type switch {
                "IHDR" => new { Width = BinaryPrimitives.ReadUInt32BigEndian(payload), Height = BinaryPrimitives.ReadUInt32BigEndian(payload[4..]), BitDepth = payload[8], ColorType = payload[9] },
                "gAMA" when size == 4 => new { Gamma = BinaryPrimitives.ReadUInt32BigEndian(payload) / 100000.0 },
                "sRGB" when size == 1 => new { RenderingIntent = payload[0] },
                "iCCP" => new { ProfileName = Encoding.Latin1.GetString(payload[..Math.Max(0, payload.IndexOf((byte)0))]), PayloadSha256 = Convert.ToHexString(SHA256.HashData(payload)) },
                "cHRM" or "cICP" or "mDCv" or "cLLi" => new { Hex = Convert.ToHexString(payload[..Math.Min(size, 64)]) },
                _ => null
            };
            chunks.Add(new { Type = type, Bytes = size, CrcValid = true, Detail = detail });
            header = true;
            dataFound |= type == "IDAT";
            offset += size + 12;
            if (type == "IEND") { if (size != 0 || offset != bytes.Length) throw new InvalidDataException("Invalid PNG end or trailing data."); end = true; break; }
        }
        if (!end || !dataFound) throw new InvalidDataException("Incomplete PNG.");
        return chunks;
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        uint value = uint.MaxValue;
        foreach (byte b in data)
        {
            value ^= b;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0 : 0xedb88320u);
        }
        return ~value;
    }
}
