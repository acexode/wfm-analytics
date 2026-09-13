using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WfmAnalytics.Collector;

public static class WindowsDpapiKeyStore
{
    public const string ProtectionDescription = "windows-dpapi-current-user";

    public static byte[] LoadOrCreateKey(string protectedKeyPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI key protection requires Windows.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(protectedKeyPath)) ?? ".");
        if (File.Exists(protectedKeyPath))
        {
            return Unprotect(File.ReadAllBytes(protectedKeyPath));
        }

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(protectedKeyPath, Protect(key));
        return key;
    }

    private static byte[] Protect(byte[] plaintext)
    {
        return InvokeDpapi(plaintext, protect: true);
    }

    private static byte[] Unprotect(byte[] protectedBytes)
    {
        return InvokeDpapi(protectedBytes, protect: false);
    }

    private static byte[] InvokeDpapi(byte[] input, bool protect)
    {
        var inputBlob = default(DataBlob);
        var outputBlob = default(DataBlob);
        try
        {
            inputBlob.CbData = input.Length;
            inputBlob.PbData = Marshal.AllocHGlobal(input.Length);
            Marshal.Copy(input, 0, inputBlob.PbData, input.Length);

            var ok = protect
                ? CryptProtectData(ref inputBlob, "wfm-collector-queue-key", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob)
                : CryptUnprotectData(ref inputBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out outputBlob);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var output = new byte[outputBlob.CbData];
            Marshal.Copy(outputBlob.PbData, output, 0, output.Length);
            return output;
        }
        finally
        {
            if (inputBlob.PbData != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(inputBlob.PbData);
            }

            if (outputBlob.PbData != IntPtr.Zero)
            {
                _ = LocalFree(outputBlob.PbData);
            }
        }
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(
        ref DataBlob pDataIn,
        string? szDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(
        ref DataBlob pDataIn,
        IntPtr ppszDataDescr,
        IntPtr pOptionalEntropy,
        IntPtr pvReserved,
        IntPtr pPromptStruct,
        int dwFlags,
        out DataBlob pDataOut);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int CbData;
        public IntPtr PbData;
    }
}
