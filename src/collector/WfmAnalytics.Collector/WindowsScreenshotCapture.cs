using System.Runtime.InteropServices;

namespace WfmAnalytics.Collector;

public static class WindowsScreenshotCapture
{
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;
    private const int SmCxScreen = 0;
    private const int SmCyScreen = 1;
    private const int Srccopy = 0x00CC0020;
    private const int CaptureBlt = 0x40000000;
    private const int BiRgb = 0;
    private const uint DibRgbColors = 0;

    public static byte[]? TryCaptureDesktopBmp()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        if (width <= 0 || height <= 0)
        {
            width = GetSystemMetrics(SmCxScreen);
            height = GetSystemMetrics(SmCyScreen);
            left = 0;
            top = 0;
            if (width <= 0 || height <= 0)
            {
                return null;
            }
        }

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return null;
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var oldObject = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);
            bitmap = CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return null;
            }

            oldObject = SelectObject(memoryDc, bitmap);
            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, Srccopy | CaptureBlt))
            {
                return null;
            }

            return CreateBitmapBytes(screenDc, bitmap, width, height);
        }
        finally
        {
            if (oldObject != IntPtr.Zero)
            {
                _ = SelectObject(memoryDc, oldObject);
            }

            if (bitmap != IntPtr.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != IntPtr.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            _ = ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static byte[] CreateBitmapBytes(IntPtr screenDc, IntPtr bitmap, int width, int height)
    {
        var header = new BitmapInfoHeader
        {
            BiSize = Marshal.SizeOf<BitmapInfoHeader>(),
            BiWidth = width,
            BiHeight = -height,
            BiPlanes = 1,
            BiBitCount = 32,
            BiCompression = BiRgb,
            BiSizeImage = width * height * 4
        };

        var pixels = new byte[header.BiSizeImage];
        var result = GetDIBits(screenDc, bitmap, 0, (uint)height, pixels, ref header, DibRgbColors);
        if (result == 0)
        {
            throw new InvalidOperationException("Windows screenshot capture failed while reading bitmap bits.");
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var pixelOffset = 14 + header.BiSize;
        var fileSize = pixelOffset + pixels.Length;
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(fileSize);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(pixelOffset);
        writer.Write(header.BiSize);
        writer.Write(header.BiWidth);
        writer.Write(header.BiHeight);
        writer.Write(header.BiPlanes);
        writer.Write(header.BiBitCount);
        writer.Write(header.BiCompression);
        writer.Write(header.BiSizeImage);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(pixels);
        writer.Flush();
        return stream.ToArray();
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int cx, int cy);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr ho);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, int rop);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, byte[] lpvBits, ref BitmapInfoHeader lpbmi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int BiSize;
        public int BiWidth;
        public int BiHeight;
        public short BiPlanes;
        public short BiBitCount;
        public int BiCompression;
        public int BiSizeImage;
        public int BiXPelsPerMeter;
        public int BiYPelsPerMeter;
        public int BiClrUsed;
        public int BiClrImportant;
    }
}
