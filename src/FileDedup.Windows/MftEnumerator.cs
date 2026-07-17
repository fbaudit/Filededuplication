using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using FileDedup.Core.Search;
using Microsoft.Win32.SafeHandles;

namespace FileDedup.Windows;

/// <summary>
/// NTFS MFT를 FSCTL_ENUM_USN_DATA로 직접 열거한다 (Everything과 동일 원리).
/// 디렉터리 재귀 없이 볼륨 전체 파일명을 수 초 내에 읽는다. 관리자 권한 필요.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MftEnumerator : IVolumeEnumerator
{
    public bool IsSupported(string driveRoot)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var handle = OpenVolume(driveRoot);
            return !handle.IsInvalid;
        }
        catch
        {
            return false;
        }
    }

    public List<VolumeItem> Enumerate(string driveRoot, CancellationToken ct = default)
    {
        var items = new List<VolumeItem>(1 << 18);
        using var volume = OpenVolume(driveRoot);
        if (volume.IsInvalid)
            throw new IOException($"볼륨을 열 수 없습니다 (관리자 권한 필요): {driveRoot}");

        // MFT_ENUM_DATA_V0: StartFileReferenceNumber(8) + LowUsn(8) + HighUsn(8)
        var input = new byte[24];
        BinaryPrimitives.WriteInt64LittleEndian(input.AsSpan(16), long.MaxValue);

        var output = new byte[1024 * 1024];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!DeviceIoControl(volume, FSCTL_ENUM_USN_DATA,
                    input, input.Length, output, output.Length, out var bytesReturned, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ERROR_HANDLE_EOF) break;
                throw new IOException($"MFT 열거 실패 (Win32 오류 {error})");
            }
            if (bytesReturned < 8) break;

            var span = output.AsSpan(0, bytesReturned);
            // 응답 선두 8바이트 = 다음 시작 FRN
            span[..8].CopyTo(input);

            var offset = 8;
            while (offset + 60 <= bytesReturned)
            {
                var recordLength = BinaryPrimitives.ReadInt32LittleEndian(span[offset..]);
                if (recordLength <= 0 || offset + recordLength > bytesReturned) break;

                var frn = BinaryPrimitives.ReadUInt64LittleEndian(span[(offset + 8)..]);
                var parentFrn = BinaryPrimitives.ReadUInt64LittleEndian(span[(offset + 16)..]);
                var attributes = BinaryPrimitives.ReadUInt32LittleEndian(span[(offset + 52)..]);
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 56)..]);
                var nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(span[(offset + 58)..]);

                if (offset + nameOffset + nameLength <= bytesReturned)
                {
                    var name = Encoding.Unicode.GetString(
                        span.Slice(offset + nameOffset, nameLength));
                    items.Add(new VolumeItem(
                        frn, parentFrn, name,
                        (attributes & FILE_ATTRIBUTE_DIRECTORY) != 0));
                }
                offset += recordLength;
            }
        }
        return items;
    }

    private static SafeFileHandle OpenVolume(string driveRoot)
    {
        var letter = Path.GetPathRoot(Path.GetFullPath(driveRoot))!
            .TrimEnd('\\', '/');
        return CreateFile($@"\\.\{letter}", GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
            OPEN_EXISTING, 0, IntPtr.Zero);
    }

    private const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
    private const uint GENERIC_READ = 0x80000000;
    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int ERROR_HANDLE_EOF = 38;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode,
        IntPtr lpSecurityAttributes, uint dwCreationDisposition,
        uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, int nInBufferSize,
        byte[] lpOutBuffer, int nOutBufferSize,
        out int lpBytesReturned, IntPtr lpOverlapped);
}
