using System.Runtime.InteropServices;

namespace WfmAnalytics.Collector;

public static class WindowsClipboardReader
{
    private const uint CfUnicodeText = 13;
    private const int MaxClipboardChars = 4096;

    public static string? TryReadText()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (!IsClipboardFormatAvailable(CfUnicodeText) || !OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CfUnicodeText);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var text = Marshal.PtrToStringUni(pointer);
                if (string.IsNullOrEmpty(text))
                {
                    return null;
                }

                return text.Length <= MaxClipboardChars ? text : text[..MaxClipboardChars];
            }
            finally
            {
                _ = GlobalUnlock(handle);
            }
        }
        finally
        {
            _ = CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
