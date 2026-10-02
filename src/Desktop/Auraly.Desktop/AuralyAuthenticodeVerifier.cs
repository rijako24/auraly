using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Auraly.Desktop;

internal static class AuralyAuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 =
        new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static bool IsValid(string path, string expectedThumbprint)
    {
        var fileInfo = new WinTrustFileInfo(path);
        var data = new WinTrustData(fileInfo);
        try
        {
            var status = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data);
            if (status != 0) return false;

            using var signedCertificate = X509Certificate.CreateFromSignedFile(path);
            using var certificate = new X509Certificate2(signedCertificate);
            return string.Equals(
                certificate.Thumbprint,
                expectedThumbprint,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            return false;
        }
        finally
        {
            data.StateAction = WinTrustDataStateAction.Close;
            _ = WinVerifyTrust(IntPtr.Zero, GenericVerifyV2, data);
            data.Dispose();
            fileInfo.Dispose();
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(
        IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        WinTrustData data);

    private enum WinTrustDataUiChoice : uint
    {
        None = 2
    }

    private enum WinTrustDataRevocationChecks : uint
    {
        WholeChain = 1
    }

    private enum WinTrustDataChoice : uint
    {
        File = 1
    }

    private enum WinTrustDataStateAction : uint
    {
        Ignore = 0,
        Verify = 1,
        Close = 2
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo : IDisposable
    {
        public WinTrustFileInfo(string path)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            FilePath = Marshal.StringToCoTaskMemUni(path);
        }

        // All instance fields participate in native layout, including private
        // fields. cbStruct must be first, exactly as in WINTRUST_FILE_INFO.
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle = IntPtr.Zero;
        public IntPtr KnownSubject = IntPtr.Zero;

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(FilePath);
            FilePath = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData : IDisposable
    {
        public WinTrustData(WinTrustFileInfo fileInfo)
        {
            StructSize = (uint)Marshal.SizeOf<WinTrustData>();
            UiChoice = WinTrustDataUiChoice.None;
            RevocationChecks = WinTrustDataRevocationChecks.WholeChain;
            UnionChoice = WinTrustDataChoice.File;
            StateAction = WinTrustDataStateAction.Verify;
            ProviderFlags = 0x00000040;
            File = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, File, false);
        }

        // Keep only the WINTRUST_DATA fields in their Windows ABI order.
        public uint StructSize;
        public IntPtr PolicyCallbackData = IntPtr.Zero;
        public IntPtr SipClientData = IntPtr.Zero;
        public WinTrustDataUiChoice UiChoice;
        public WinTrustDataRevocationChecks RevocationChecks;
        public WinTrustDataChoice UnionChoice;
        public IntPtr File;
        public WinTrustDataStateAction StateAction;
        public IntPtr StateData = IntPtr.Zero;
        public IntPtr UrlReference = IntPtr.Zero;
        public uint ProviderFlags;
        public uint UiContext = 0;
        public IntPtr SignatureSettings = IntPtr.Zero;

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(File);
            File = IntPtr.Zero;
        }
    }
}
