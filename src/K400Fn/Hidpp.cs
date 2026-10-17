using System.Text;
using K400Fn.Hid;

namespace K400Fn.Hidpp;

internal enum FnMode
{
    FunctionKeys = 0,
    MediaKeys = 1,
    Unknown = 0xFF
}

internal readonly record struct FnState(FnMode Mode, FnMode? Default)
{
    public string ModeName => Mode switch
    {
        FnMode.FunctionKeys => "F-keys",
        FnMode.MediaKeys => "media keys",
        _ => "unknown"
    };

    public string DefaultName => Default switch
    {
        FnMode.FunctionKeys => "F-keys",
        FnMode.MediaKeys => "media keys",
        _ => "unknown"
    };
}

internal readonly record struct HidppReply(byte[] Payload, byte? Error)
{
    public bool IsError => Error is not null;
}

/// <summary>
/// One HID++ 2.0 device. This may be a receiver itself (device index 0xFF)
/// or a peripheral paired to a Unifying/Bolt receiver (index 1-6).
/// </summary>
internal sealed class HidppDevice
{
    private const byte SoftwareId = 0x0B;

    // Feature IDs from Logitech's HID++ 2.0 specification.
    private const ushort RootFeature = 0x0000;
    private const ushort DeviceNameFeature = 0x0005;
    private const ushort FnInversionFeature = 0x40A0;
    private const ushort NewFnInversionFeature = 0x40A2;

    private readonly HidConnection _connection;

    public byte DeviceIndex { get; }

    private HidppDevice(HidConnection connection, byte deviceIndex)
    {
        _connection = connection;
        DeviceIndex = deviceIndex;
    }

    public static HidppDevice? TryCreate(HidConnection connection, byte deviceIndex)
    {
        var device = new HidppDevice(connection, deviceIndex);
        return device.Ping() ? device : null;
    }

    private bool Ping()
    {
        // IRoot, function 1: get protocol version. A reply means the device is
        // present and awake.
        byte[] parameters = [0x00, 0x00, (byte)Random.Shared.Next(1, 255)];
        var reply = Request((byte)RootFeature, 0x01, parameters, timeoutMs: 1500, attempts: 1);
        return reply is { IsError: false, Payload.Length: >= 2 };
    }

    public byte? GetFeature(ushort featureId)
    {
        byte[] parameters = [(byte)(featureId >> 8), (byte)featureId];
        var reply = Request((byte)RootFeature, 0x00, parameters, timeoutMs: 800, attempts: 1);

        if (reply is null || reply.Value.IsError || reply.Value.Payload.Length == 0)
            return null;

        byte index = reply.Value.Payload[0];
        return index == 0 ? null : index;
    }

    /// <summary>Finds the Fn-inversion feature. Newer K400 Plus firmware uses 0x40A2.</summary>
    public (byte Index, ushort Id)? FindFnInversionFeature()
    {
        byte? index = GetFeature(NewFnInversionFeature);
        if (index is not null)
            return (index.Value, NewFnInversionFeature);

        index = GetFeature(FnInversionFeature);
        if (index is not null)
            return (index.Value, FnInversionFeature);

        return null;
    }

    public FnState? ReadFnState(byte featureIndex)
    {
        var reply = Request(featureIndex, 0x00, [0x00, 0x00, 0x00], timeoutMs: 800, attempts: 1);
        if (reply is null || reply.Value.IsError || reply.Value.Payload.Length < 1)
            return null;

        FnMode mode = reply.Value.Payload[0] switch
        {
            0 => FnMode.FunctionKeys,
            1 => FnMode.MediaKeys,
            _ => FnMode.Unknown
        };

        if (mode == FnMode.Unknown)
            return null;

        FnMode? defaultValue = reply.Value.Payload.Length >= 2
            ? reply.Value.Payload[1] switch
            {
                0 => FnMode.FunctionKeys,
                1 => FnMode.MediaKeys,
                _ => null
            }
            : null;

        return new FnState(mode, defaultValue);
    }

    public bool SetFnState(byte featureIndex, FnMode mode)
    {
        if (mode is not (FnMode.FunctionKeys or FnMode.MediaKeys))
            return false;

        byte[] parameters = [(byte)mode, 0x00, 0x00];
        var reply = Request(featureIndex, 0x01, parameters, timeoutMs: 1000, attempts: 2);

        // Some devices acknowledge a write with a one-byte result; others just
        // echo the new state. Either is fine. If no reply arrives, read back.
        if (reply is { IsError: false })
            return true;

        FnState? state = ReadFnState(featureIndex);
        return state?.Mode == mode;
    }

    public string? GetDeviceName()
    {
        byte? feature = GetFeature(DeviceNameFeature);
        if (feature is null)
            return null;

        var lengthReply = Request(feature.Value, 0x00, [], timeoutMs: 800, attempts: 1);
        if (lengthReply is null || lengthReply.Value.IsError || lengthReply.Value.Payload.Length == 0)
            return null;

        int length = lengthReply.Value.Payload[0];
        if (length <= 0 || length > 128)
            return null;

        var name = new StringBuilder(length);
        while (name.Length < length)
        {
            byte offset = (byte)name.Length;
            var fragment = Request(feature.Value, 0x01, [offset, 0x00, 0x00], timeoutMs: 800, attempts: 1);
            if (fragment is null || fragment.Value.IsError || fragment.Value.Payload.Length == 0)
                break;

            int take = Math.Min(fragment.Value.Payload.Length, length - name.Length);
            if (take <= 0)
                break;

            name.Append(Encoding.UTF8.GetString(fragment.Value.Payload, 0, take));
        }

        string result = name.ToString().TrimEnd('\0').Trim();
        if (result.Length == 0)
            return null;

        // Guard against a misparsed binary payload.
        foreach (char c in result)
        {
            if (char.IsControl(c))
                return null;
        }

        return result;
    }

    private HidppReply? Request(
        byte featureIndex,
        byte function,
        ReadOnlySpan<byte> parameters,
        int timeoutMs,
        int attempts)
    {
        byte requestId = (byte)((function << 4) | SoftwareId);
        byte[]? frame = BuildFrame(featureIndex, requestId, parameters);
        if (frame is null)
            return null;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            _connection.Drain();

            if (!_connection.Write(frame, frame.Length))
                continue;

            long deadline = Environment.TickCount64 + timeoutMs;
            while (true)
            {
                int remaining = (int)(deadline - Environment.TickCount64);
                if (remaining <= 0)
                    break;

                byte[]? report = _connection.Read(remaining);
                if (report is null)
                    break;

                HidppReply? reply = Parse(report, featureIndex, requestId);
                if (reply is not null)
                    return reply;
            }
        }

        return null;
    }

    private byte[]? BuildFrame(byte featureIndex, byte requestId, ReadOnlySpan<byte> parameters)
    {
        // Prefer a long (20-byte) report. A few HID++ collections only expose
        // the short report; fall back to that when the descriptor says so.
        if (_connection.OutputReportLength >= 20)
        {
            if (parameters.Length > 16)
                return null;

            var frame = new byte[20];
            frame[0] = 0x11;
            frame[1] = DeviceIndex;
            frame[2] = featureIndex;
            frame[3] = requestId;
            parameters.CopyTo(frame.AsSpan(4));
            return frame;
        }

        if (parameters.Length > 3)
            return null;

        var shortFrame = new byte[7];
        shortFrame[0] = 0x10;
        shortFrame[1] = DeviceIndex;
        shortFrame[2] = featureIndex;
        shortFrame[3] = requestId;
        parameters.CopyTo(shortFrame.AsSpan(4));
        return shortFrame;
    }

    private HidppReply? Parse(byte[] report, byte featureIndex, byte requestId)
    {
        if (report.Length < 4)
            return null;

        if (report[0] is not (0x10 or 0x11))
            return null;

        // Bluetooth-connected devices sometimes answer with device index 0
        // when addressed as 0xFF.
        byte expectedAlternate = (byte)(DeviceIndex ^ 0xFF);
        if (report[1] != DeviceIndex && report[1] != expectedAlternate)
            return null;

        // Normal reply: [feature, request-id, payload...]
        if (report[2] == featureIndex && report[3] == requestId)
            return new HidppReply(report[4..], null);

        // HID++ 2.0 error reply: [0xFF, feature, request-id, error]
        if (report.Length >= 6 &&
            report[2] == 0xFF &&
            report[3] == featureIndex &&
            report[4] == requestId)
            return new HidppReply([], report[5]);

        // HID++ 1.0 error reply: [0x8F, feature, request-id, error]
        if (report.Length >= 6 &&
            report[2] == 0x8F &&
            report[3] == featureIndex &&
            report[4] == requestId)
            return new HidppReply([], report[5]);

        return null;
    }
}
