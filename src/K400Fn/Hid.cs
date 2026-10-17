using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace K400Fn.Hid;

internal sealed record HidInterfaceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    string ProductName,
    ushort UsagePage,
    ushort Usage,
    ushort InputReportLength,
    ushort OutputReportLength);

/// <summary>
/// Minimal HID access built on the Windows HID/setupapi DLLs, so the tool has
/// no runtime dependencies beyond the .NET runtime (or nothing at all when
/// published self-contained).
/// </summary>
internal static class HidNative
{
    internal const uint DIGCF_PRESENT = 0x02;
    internal const uint DIGCF_DEVICEINTERFACE = 0x10;

    internal static readonly Guid HidInterfaceClass =
        new("4D1E55B2-F16F-11CF-88CB-001111000030");

    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x01;
    internal const uint FILE_SHARE_WRITE = 0x02;
    internal const uint OPEN_EXISTING = 0x03;
    internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    internal const int ERROR_NO_MORE_ITEMS = 259;
    internal const int ERROR_IO_PENDING = 997;

    internal const uint WAIT_OBJECT_0 = 0x00000000;
    internal const uint WAIT_TIMEOUT = 0x00000102;

    internal const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SpDeviceInterfaceData
    {
        public int Size;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HiddAttributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;

        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Overlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetAttributes(SafeFileHandle device, ref HiddAttributes attributes);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    internal static extern int HidP_GetCaps(IntPtr preparsedData, ref HidpCaps capabilities);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool HidD_GetProductString(
        SafeFileHandle device,
        [Out] char[] buffer,
        int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(
        SafeFileHandle file,
        IntPtr buffer,
        uint bytesToRead,
        out uint bytesRead,
        ref Overlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteFile(
        SafeFileHandle file,
        IntPtr buffer,
        uint bytesToWrite,
        out uint bytesWritten,
        ref Overlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetOverlappedResult(
        SafeFileHandle file,
        ref Overlapped overlapped,
        out uint bytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CancelIoEx(SafeFileHandle file, ref Overlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ResetEvent(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}

internal static class HidEnumerator
{
    public static IReadOnlyList<HidInterfaceInfo> Enumerate(bool includeAllLogitechCollections = false)
    {
        var results = new List<HidInterfaceInfo>();
        var guid = HidNative.HidInterfaceClass;
        IntPtr deviceInfoSet = IntPtr.Zero;

        try
        {
            deviceInfoSet = HidNative.SetupDiGetClassDevs(
                ref guid,
                IntPtr.Zero,
                IntPtr.Zero,
                HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);

            if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
                return results;

            var interfaceData = new HidNative.SpDeviceInterfaceData
            {
                Size = Marshal.SizeOf<HidNative.SpDeviceInterfaceData>()
            };

            uint index = 0;
            while (HidNative.SetupDiEnumDeviceInterfaces(
                       deviceInfoSet, IntPtr.Zero, ref guid, index++, ref interfaceData))
            {
                int requiredSize;
                HidNative.SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);

                if (requiredSize <= 0)
                    continue;

                IntPtr detail = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    // cbSize is 8 on 64-bit Windows, 6 on 32-bit. The path
                    // itself always starts at offset 4 (right after the DWORD).
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                    if (!HidNative.SetupDiGetDeviceInterfaceDetail(
                            deviceInfoSet, ref interfaceData, detail, requiredSize, out _, IntPtr.Zero))
                        continue;

                    string path = Marshal.PtrToStringUni(IntPtr.Add(detail, 4)) ?? string.Empty;
                    if (path.Length == 0)
                        continue;

                    var info = Probe(path);
                    if (info is not null &&
                        (includeAllLogitechCollections ||
                         (info.UsagePage == 0xFF00 && info.Usage is 0x0001 or 0x0002)))
                        results.Add(info);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            if (deviceInfoSet != IntPtr.Zero && deviceInfoSet != new IntPtr(-1))
                HidNative.SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }

    private static HidInterfaceInfo? Probe(string path)
    {
        using SafeFileHandle handle = HidNative.CreateFile(
            path,
            HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            HidNative.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            return null;

        var attributes = new HidNative.HiddAttributes
        {
            Size = Marshal.SizeOf<HidNative.HiddAttributes>()
        };

        if (!HidNative.HidD_GetAttributes(handle, ref attributes))
            return null;

        // Logitech only.
        if (attributes.VendorId != 0x046D)
            return null;

        if (!HidNative.HidD_GetPreparsedData(handle, out IntPtr preparsedData))
            return null;

        try
        {
            var caps = new HidNative.HidpCaps { Reserved = new ushort[17] };
            int status = HidNative.HidP_GetCaps(preparsedData, ref caps);
            if (status != HidNative.HIDP_STATUS_SUCCESS)
                return null;

            char[] product = new char[128];
            string productName = HidNative.HidD_GetProductString(handle, product, product.Length * 2)
                ? new string(product).TrimEnd('\0')
                : string.Empty;

            return new HidInterfaceInfo(
                path,
                attributes.VendorId,
                attributes.ProductId,
                productName,
                caps.UsagePage,
                caps.Usage,
                caps.InputReportByteLength,
                caps.OutputReportByteLength);
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsedData);
        }
    }
}

internal sealed class HidConnection : IDisposable
{
    private readonly SafeFileHandle _handle;
    private readonly IntPtr _event;
    private readonly int _inputReportLength;

    public HidInterfaceInfo Info { get; }
    public int OutputReportLength { get; }

    private HidConnection(SafeFileHandle handle, HidInterfaceInfo info)
    {
        _handle = handle;
        Info = info;
        OutputReportLength = info.OutputReportLength > 0 ? info.OutputReportLength : 20;
        _inputReportLength = info.InputReportLength > 0 ? info.InputReportLength : 32;
        _event = HidNative.CreateEvent(IntPtr.Zero, false, false, null);
    }

    public static HidConnection? TryOpen(HidInterfaceInfo info)
    {
        SafeFileHandle handle = HidNative.CreateFile(
            info.Path,
            HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE,
            IntPtr.Zero,
            HidNative.OPEN_EXISTING,
            HidNative.FILE_FLAG_OVERLAPPED,
            IntPtr.Zero);

        return handle.IsInvalid ? null : new HidConnection(handle, info);
    }

    public bool Write(byte[] report, int length)
    {
        IntPtr buffer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.Copy(report, 0, buffer, length);
            HidNative.ResetEvent(_event);
            var overlapped = new HidNative.Overlapped { Event = _event };

            if (!HidNative.WriteFile(_handle, buffer, (uint)length, out uint written, ref overlapped))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != HidNative.ERROR_IO_PENDING)
                    return false;

                uint wait = HidNative.WaitForSingleObject(_event, 1000);
                if (wait != HidNative.WAIT_OBJECT_0)
                {
                    HidNative.CancelIoEx(_handle, ref overlapped);
                    HidNative.WaitForSingleObject(_event, 1000);
                    return false;
                }

                if (!HidNative.GetOverlappedResult(_handle, ref overlapped, out written, false))
                    return false;
            }

            return written == (uint)length;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Reads one input report. Returns null on timeout.</summary>
    public byte[]? Read(int timeoutMs)
    {
        IntPtr buffer = Marshal.AllocHGlobal(_inputReportLength);
        try
        {
            HidNative.ResetEvent(_event);
            var overlapped = new HidNative.Overlapped { Event = _event };

            uint read = 0;
            if (!HidNative.ReadFile(_handle, buffer, (uint)_inputReportLength, out read, ref overlapped))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != HidNative.ERROR_IO_PENDING)
                    return null;

                uint wait = HidNative.WaitForSingleObject(_event, (uint)Math.Max(0, timeoutMs));
                if (wait != HidNative.WAIT_OBJECT_0)
                {
                    // Timeout. Cancel the pending read, then wait for the
                    // driver to release the buffer before freeing it.
                    HidNative.CancelIoEx(_handle, ref overlapped);
                    HidNative.WaitForSingleObject(_event, 1000);
                    return null;
                }

                if (!HidNative.GetOverlappedResult(_handle, ref overlapped, out read, false))
                    return null;
            }

            if (read == 0)
                return null;

            var result = new byte[read];
            Marshal.Copy(buffer, result, 0, (int)read);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Drain()
    {
        // Deliveries can be queued while the device is idle. Discard them so a
        // late reply cannot be mistaken for the answer to a new request.
        for (int i = 0; i < 32; i++)
        {
            byte[]? stale = Read(0);
            if (stale is null)
                return;
        }
    }

    public void Dispose()
    {
        if (_event != IntPtr.Zero)
            HidNative.CloseHandle(_event);
        _handle.Dispose();
    }
}
