using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NovelShelf.Services;

public static class TextFileReader
{
    private const uint Gb18030 = 54936;
    private const uint Gbk = 936;

    public static string Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0)
        {
            return "";
        }

        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF))
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        if (HasPrefix(bytes, 0xFF, 0xFE))
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (HasPrefix(bytes, 0xFE, 0xFF))
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }

        if (TryDecodeUtf8(bytes, out var utf8Text))
        {
            return utf8Text;
        }

        if (TryDecodeWindowsCodePage(bytes, Gb18030, out var gb18030Text))
        {
            return gb18030Text;
        }

        if (TryDecodeWindowsCodePage(bytes, Gbk, out var gbkText))
        {
            return gbkText;
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static bool TryDecodeUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = "";
            return false;
        }
    }

    private static bool TryDecodeWindowsCodePage(byte[] bytes, uint codePage, out string text)
    {
        var length = MultiByteToWideChar(codePage, 0, bytes, bytes.Length, null, 0);
        if (length <= 0)
        {
            text = "";
            return false;
        }

        var chars = new char[length];
        var written = MultiByteToWideChar(codePage, 0, bytes, bytes.Length, chars, chars.Length);
        if (written <= 0)
        {
            text = "";
            return false;
        }

        text = new string(chars, 0, written);
        return true;
    }

    private static bool HasPrefix(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (bytes[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int MultiByteToWideChar(
        uint codePage,
        uint flags,
        byte[] multiByteString,
        int byteCount,
        [Out] char[]? wideCharString,
        int wideCharCount);
}

