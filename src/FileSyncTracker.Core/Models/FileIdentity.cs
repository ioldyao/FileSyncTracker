using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileSyncTracker.Core.Models;

public class FileIdentity
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }
    public string? ContentHash { get; set; }
    public long NtfsFileId { get; set; }
    public uint VolumeSerialNumber { get; set; }

    public static FileIdentity FromFile(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        GetFileIdentity(filePath, out var fileId, out var volumeSerial);
        return new FileIdentity
        {
            FileName = fileInfo.Name,
            FileSize = fileInfo.Length,
            LastModified = fileInfo.LastWriteTime,
            NtfsFileId = fileId,
            VolumeSerialNumber = volumeSerial
        };
    }

    public bool Matches(FileIdentity other)
    {
        if (NtfsFileId != 0 && other.NtfsFileId != 0 && NtfsFileId != other.NtfsFileId)
            return false;

        if (NtfsFileId != 0 && other.NtfsFileId != 0 && NtfsFileId == other.NtfsFileId)
            return true;

        return FileName == other.FileName
            && FileSize == other.FileSize
            && LastModified == other.LastModified;
    }

    public bool FallbackMatch(FileIdentity other)
    {
        return FileName == other.FileName && FileSize == other.FileSize;
    }

    /// <summary>
    /// Resolve file path directly by VolumeSerialNumber + FileId using OpenFileById API.
    /// This works even if the file was renamed during move.
    /// </summary>
    public string? ResolvePathById()
    {
        if (NtfsFileId == 0 || VolumeSerialNumber == 0) return null;

        Console.WriteLine($"[FileId] ResolvePathById: FileId={NtfsFileId}, VolumeSerial={VolumeSerialNumber}");

        // Try all mounted volumes
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveFormat != "NTFS") continue;

            try
            {
                var volPath = $@"\\.\{drive.Name[0]}:";
                using var volHandle = CreateFile(
                    volPath,
                    0,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_BACKUP_SEMANTICS,
                    IntPtr.Zero);

                if (volHandle.IsInvalid)
                {
                    Console.WriteLine($"[FileId] Volume handle invalid: {volPath}");
                    continue;
                }

                // Verify volume serial matches
                if (!GetVolumeInformation(volPath, null, 0, out var serial, out _, out _, null, 0))
                {
                    Console.WriteLine($"[FileId] GetVolumeInformation failed: {volPath}");
                    continue;
                }

                if (serial != VolumeSerialNumber)
                {
                    Console.WriteLine($"[FileId] Volume serial mismatch: {volPath} has {serial}, expected {VolumeSerialNumber}");
                    continue;
                }

                Console.WriteLine($"[FileId] Volume matched: {volPath}, trying OpenFileById");

                var desc = new FILE_ID_DESCRIPTOR
                {
                    dwSize = 24,
                    type = 0,
                    FileId = NtfsFileId
                };

                using var fileHandle = OpenFileById(
                    volHandle,
                    ref desc,
                    0x80000000, // GENERIC_READ
                    (uint)(FileShare.ReadWrite | FileShare.Delete),
                    IntPtr.Zero,
                    FILE_FLAG_BACKUP_SEMANTICS);

                if (fileHandle.IsInvalid)
                {
                    Console.WriteLine($"[FileId] OpenFileById failed: {Marshal.GetLastWin32Error()}");
                    continue;
                }

                var sb = new StringBuilder(512);
                var ret = GetFinalPathNameByHandle(fileHandle, sb, 512, 0);
                if (ret == 0)
                {
                    Console.WriteLine($"[FileId] GetFinalPathNameByHandle failed: {Marshal.GetLastWin32Error()}");
                    continue;
                }

                var path = sb.ToString();
                if (path.StartsWith(@"\\?\"))
                    path = path.Substring(4);

                Console.WriteLine($"[FileId] Resolved path: {path}");

                if (File.Exists(path))
                {
                    return path;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FileId] Exception on {drive.Name}: {ex.Message}");
            }
        }

        return null;
    }

    private static void GetFileIdentity(string filePath, out long fileId, out uint volumeSerial)
    {
        fileId = 0;
        volumeSerial = 0;
        try
        {
            using var handle = CreateFile(
                filePath,
                0,
                FileShare.ReadWrite | FileShare.Delete,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (handle.IsInvalid) return;

            if (GetFileInformationByHandle(handle, out var info))
            {
                fileId = ((long)info.nFileIndexHigh << 32) | info.nFileIndexLow;
                volumeSerial = info.dwVolumeSerialNumber;
            }
        }
        catch { }
    }

    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        FileShare dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle OpenFileById(
        SafeFileHandle hVolumeHint,
        ref FILE_ID_DESCRIPTOR lpFileId,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwFlagsAndAttributes);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        [MarshalAs(UnmanagedType.LPStr)] StringBuilder? lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        [MarshalAs(UnmanagedType.LPStr)] StringBuilder? lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential, Size = 24)]
    private struct FILE_ID_DESCRIPTOR
    {
        public uint dwSize;
        public uint type;
        public long FileId;
    }
}
