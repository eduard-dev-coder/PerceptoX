using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PerceptoX.Infrastructure.Cleanup;

/// <summary>Renames the verified open file itself; outside writers and deleters are denied for its lifetime.</summary>
internal static class WindowsFileRename
{
    public static FileStream Open(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Recoverable cleanup requires Windows handle-based rename.");
        const uint genericReadAndDelete = 0x80000000 | 0x00010000;
        const uint shareRead = 1, openExisting = 3, openReparsePoint = 0x00200000;
        SafeFileHandle handle = CreateFile(path, genericReadAndDelete, shareRead, IntPtr.Zero, openExisting, openReparsePoint, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw new IOException($"Cannot lock cleanup source: {path}", new Win32Exception(error));
        }
        try
        {
            if ((File.GetAttributes(handle) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException($"Cleanup source is a link or directory: {path}");
            return new FileStream(handle, FileAccess.Read);
        }
        catch { handle.Dispose(); throw; }
    }

    public static void Move(SafeFileHandle sourceHandle, string destination)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Recoverable cleanup requires Windows handle-based rename.");
        // FILE_RENAME_INFO: union DWORD at0; aligned HANDLE at4(x86)/8(x64); DWORD byte length; WCHAR name.
        // The zero-initialized union means ReplaceIfExists=FALSE. RootDirectory=NULL; absolute DOS destination.
        int rootOffset = IntPtr.Size == 8 ? 8 : 4;
        int lengthOffset = rootOffset + IntPtr.Size;
        int nameOffset = lengthOffset + sizeof(uint);
        byte[] filename = Encoding.Unicode.GetBytes(Path.GetFullPath(destination));
        byte[] information = new byte[nameOffset + filename.Length + sizeof(char)];
        BinaryPrimitives.WriteUInt32LittleEndian(information.AsSpan(lengthOffset), checked((uint)filename.Length));
        filename.CopyTo(information.AsSpan(nameOffset));
        if (!SetFileInformationByHandle(sourceHandle, 3, information, checked((uint)information.Length)))
            throw new IOException($"Cannot archive/restore verified file without overwrite: {destination}", new Win32Exception(Marshal.GetLastWin32Error()));
    }

    // DllImport keeps this tiny interop boundary safe-code-only; LibraryImport would require /unsafe in the project.
#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, [In] byte[] information, uint size);
#pragma warning restore SYSLIB1054
}
