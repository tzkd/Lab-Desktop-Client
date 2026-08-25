using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LabDesktop.Client.App.Security;

internal sealed class WindowsCredentialStore : ISshCredentialStore
{
    private const uint CredentialTypeGeneric = 1;
    private const uint CredentialPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 5 * 512;
    private const string TargetPrefix = "LabDesktopClient/ssh/";

    public string? Read(string routeIdentity)
    {
        var target = BuildTargetName(routeIdentity);
        if (!CredRead(target, CredentialTypeGeneric, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "无法读取 Windows 凭据管理器。");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            return Marshal.PtrToStringUni(
                credential.CredentialBlob,
                checked((int)credential.CredentialBlobSize / sizeof(char)));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string routeIdentity, string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        ArgumentNullException.ThrowIfNull(password);
        var secret = Encoding.Unicode.GetBytes(password);
        if (secret.Length > MaximumCredentialBlobBytes)
        {
            CryptographicOperations.ZeroMemory(secret);
            throw new ArgumentException("SSH 密码超过 Windows 凭据管理器允许的长度。", nameof(password));
        }

        var handle = GCHandle.Alloc(secret, GCHandleType.Pinned);
        try
        {
            var credential = new NativeCredential
            {
                Type = CredentialTypeGeneric,
                TargetName = BuildTargetName(routeIdentity),
                CredentialBlobSize = checked((uint)secret.Length),
                CredentialBlob = handle.AddrOfPinnedObject(),
                Persist = CredentialPersistLocalMachine,
                UserName = userName
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "无法写入 Windows 凭据管理器。");
            }
        }
        finally
        {
            handle.Free();
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public void Delete(string routeIdentity)
    {
        var target = BuildTargetName(routeIdentity);
        if (CredDelete(target, CredentialTypeGeneric, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error, "无法删除 Windows 凭据管理器中的 SSH 凭据。");
        }
    }

    internal static string BuildTargetName(string routeIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeIdentity);
        return TargetPrefix + routeIdentity.Trim().ToLowerInvariant();
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(
        string targetName,
        uint type,
        uint flags,
        out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }
}
