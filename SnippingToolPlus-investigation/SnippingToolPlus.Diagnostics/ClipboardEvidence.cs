using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace SnippingToolPlus.Diagnostics;

internal static class ClipboardEvidence
{
    internal static int HoldForTest(string eventName)
    {
        using var ready = EventWaitHandle.OpenExisting(eventName);
        if (!OpenClipboard(IntPtr.Zero)) return 2;
        // SetImage has its own WinForms retry budget inside each production attempt.
        try { ready.Set(); Thread.Sleep(15000); return 0; }
        finally { CloseClipboard(); }
    }

    internal static object Save(string output)
    {
        Directory.CreateDirectory(output);
        var formats = new List<object>();
        uint before = GetClipboardSequenceNumber();
        bool opened = false;
        for (int i = 0; i < 10 && !opened; i++) { opened = OpenClipboard(IntPtr.Zero); if (!opened) Thread.Sleep(30); }
        if (!opened) throw new ExternalException("Clipboard remained busy; no clipboard data was changed.");
        try
        {
            for (uint format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                var nameBuffer = new StringBuilder(256);
                GetClipboardFormatName(format, nameBuffer, nameBuffer.Capacity);
                string name = nameBuffer.Length > 0 ? nameBuffer.ToString() : format switch {
                    2 => "CF_BITMAP", 8 => "CF_DIB", 17 => "CF_DIBV5", _ => "Standard:" + format
                };
                string? file = null, hash = null, error = null;
                long? size = null;
                object? dibHeader = null;
                // Only HGLOBAL image formats are copied as raw bytes. CF_BITMAP is a GDI handle.
                if (format is 8 or 17 || name.Equals("PNG", StringComparison.OrdinalIgnoreCase) || name.Equals("image/png", StringComparison.OrdinalIgnoreCase))
                {
                    IntPtr handle = GetClipboardData(format);
                    ulong length = (ulong)GlobalSize(handle);
                    if (handle == IntPtr.Zero || length == 0 || length > 128 * 1024 * 1024)
                        error = "Missing, non-HGLOBAL, empty, or over 128 MiB; not copied.";
                    else
                    {
                        IntPtr pointer = GlobalLock(handle);
                        if (pointer == IntPtr.Zero) error = "GlobalLock failed: " + Marshal.GetLastWin32Error();
                        else
                        {
                            try
                            {
                                var bytes = new byte[(int)length]; Marshal.Copy(pointer, bytes, 0, bytes.Length);
                                bool png = name.Equals("PNG", StringComparison.OrdinalIgnoreCase) || name.Equals("image/png", StringComparison.OrdinalIgnoreCase);
                                file = Path.Combine(output, "native-format-" + format + (png ? ".png" : ".dib"));
                                File.WriteAllBytes(file, bytes); size = bytes.Length;
                                hash = Convert.ToHexString(SHA256.HashData(bytes));
                                if (!png && bytes.Length >= 40) dibHeader = new {
                                    HeaderBytes = BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                                    Width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)),
                                    Height = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8)),
                                    BitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(14)),
                                    Compression = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16)),
                                    ColorSpaceType = bytes.Length >= 124 && BinaryPrimitives.ReadUInt32LittleEndian(bytes) >= 124
                                        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(56)) : (uint?)null
                                };
                            }
                            finally { GlobalUnlock(handle); }
                        }
                    }
                }
                formats.Add(new { Id = format, Name = name, File = file, Bytes = size, Sha256 = hash, DibHeader = dibHeader, Error = error });
            }
        }
        finally { CloseClipboard(); }
        // This file is explicitly marked as a diagnostic re-encoding, never an original PNG.
        string? decodedFile = null, decodeError = null;
        try
        {
            using var image = Clipboard.GetImage();
            if (image != null)
            {
                decodedFile = Path.Combine(output, "decoded-bitmap-REENCODED.png");
                image.Save(decodedFile, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        catch (Exception error) { decodeError = error.Message; }
        uint after = GetClipboardSequenceNumber();
        return new { SequenceBefore = before, SequenceAfter = after, StableDuringRead = before == after,
            Formats = formats, DecodedBitmapReencoded = decodedFile, DecodeError = decodeError,
            Interpretation = "Only native-format files retain the offered image bytes. The REENCODED PNG has diagnostic metadata. A clipboard origin may itself already be a re-encoding. No clipboard writes were performed by this snapshot command."
        };
    }

    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll", SetLastError = true)] private static extern uint EnumClipboardFormats(uint previous);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClipboardFormatName(uint format, StringBuilder name, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern UIntPtr GlobalSize(IntPtr handle);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr GlobalLock(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr handle);
}

internal sealed class ClipboardLease : IDisposable
{
    private readonly DataObject previous = new();
    private readonly List<IDisposable> retained = [];
    private uint? writtenSequence;
    private bool finished;
    private readonly bool wasEmpty;

    internal ClipboardLease()
    {
        uint before = ClipboardEvidence.GetClipboardSequenceNumber();
        var data = Clipboard.GetDataObject();
        string[] formats = data?.GetFormats(false) ?? [];
        wasEmpty = formats.Length == 0;
        try
        {
            foreach (string format in formats)
            {
                object? source = data!.GetData(format, false);
                // WinForms may expose CF_BITMAP through GetImage but not GetData(..., false).
                if (source == null && format == DataFormats.Bitmap)
                {
                    using var image = Clipboard.GetImage();
                    if (image != null) source = new Bitmap(image);
                    if (source is IDisposable owned) retained.Add(owned);
                }
                object clone = source switch {
                    string text => text,
                    string[] files => files.ToArray(),
                    byte[] bytes => bytes.ToArray(),
                    MemoryStream stream => new MemoryStream(stream.ToArray()),
                    Bitmap bitmap => bitmap.Clone(),
                    _ => throw new InvalidOperationException("Cannot safely retain clipboard format '" + format + "' (" + (source?.GetType().FullName ?? "null") + "). Clipboard test aborted before writing. Use the read-only snapshot command or copy disposable test data first.")
                };
                if (clone is IDisposable disposable) retained.Add(disposable);
                previous.SetData(format, false, clone);
            }
            if (before != ClipboardEvidence.GetClipboardSequenceNumber()) throw new InvalidOperationException("Clipboard changed while retaining its contents; test aborted.");
        }
        catch { foreach (var item in retained) item.Dispose(); throw; }
    }

    internal void MarkWritten() => writtenSequence = ClipboardEvidence.GetClipboardSequenceNumber();

    internal string Restore()
    {
        if (finished) return "Already handled";
        finished = true;
        if (writtenSequence == null) return "No test clipboard write recorded";
        if (writtenSequence != ClipboardEvidence.GetClipboardSequenceNumber()) return "Another application changed the clipboard; its newer contents were preserved";
        if (wasEmpty) Clipboard.Clear();
        else Clipboard.SetDataObject(previous, true, 10, 40);
        return "Previous supported logical clipboard formats restored; not a byte-for-byte clipboard backup";
    }

    public void Dispose()
    {
        try { if (!finished) Console.WriteLine(Restore()); }
        finally { foreach (var item in retained) item.Dispose(); }
    }
}
