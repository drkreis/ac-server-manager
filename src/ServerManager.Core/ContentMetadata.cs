using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ServerManager.Core;

// Read-only decoding for game metadata; server configuration keeps strict UTF-8 handling.
public static class ContentMetadata
{
    public static string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return new UTF32Encoding(false, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return new UTF32Encoding(true, false, true).GetString(bytes, 4, bytes.Length - 4);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'); }
        catch (DecoderFallbackException)
        {
            // Legacy Kunos descriptions use Western Windows ANSI (e.g. a single-byte degree sign).
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }
    public static string PlainText(string text)
    {
        text = Regex.Replace(text, @"(?i)<br\s*/?>|</p\s*>", "\n");
        return WebUtility.HtmlDecode(Regex.Replace(text, "<[^>]*>", "")).Trim();
    }
}
