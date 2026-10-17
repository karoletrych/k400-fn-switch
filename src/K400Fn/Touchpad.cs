using K400Fn.Hidpp;

namespace K400Fn;

/// <summary>
/// Restores the K400 Plus touchpad's firmware gestures after Logi Options+ has
/// been closed. Logi Options+ diverts the "two-finger scroll" gesture to the
/// host for smooth scrolling; if the host software disappears, the keyboard
/// keeps sending HID++ notifications and stops producing normal wheel events.
/// </summary>
internal static class Touchpad
{
    private const ushort TouchpadRawXyFeature = 0x6100;
    private const ushort Gesture2Feature = 0x6501;

    private const int Scroll2Finger = 41;
    private const int Scroll2FingerHorizontal = 42;
    private const int Scroll2FingerVertical = 43;
    private const int Scroll2FingerStateless = 44;

    private sealed record GestureInfo(
        int Id,
        byte Flags,
        int? EnableIndex,
        bool? Enabled,
        int? DiversionIndex,
        bool? Diverted);

    public static int Run(bool statusOnly, bool verbose, TextWriter output, TextWriter error)
    {
        using KeyboardSession session = KeyboardSession.Discover(verbose, output);

        if (session.Targets.Count == 0)
        {
            error.WriteLine("No supported Logitech keyboard found.");
            error.WriteLine("  - Turn the keyboard on and press a key to wake it.");
            error.WriteLine("  - If Logi Options+ is running, quit it and try again.");
            return 1;
        }

        bool failed = false;

        foreach (KeyboardTarget target in session.Targets)
        {
            byte? gestureFeature = target.Device.GetFeature(Gesture2Feature);
            if (gestureFeature is null)
            {
                error.WriteLine($"{target.Name}: this device does not expose touchpad gestures.");
                failed = true;
                continue;
            }

            byte? rawFeature = target.Device.GetFeature(TouchpadRawXyFeature);
            bool? rawEnabled = null;

            if (rawFeature is not null)
            {
                var rawState = target.Device.FeatureRequest(rawFeature.Value, 1, 0x00, 0x00, 0x00);
                if (rawState is { IsError: false, Payload.Length: >= 1 })
                    rawEnabled = (rawState.Value.Payload[0] & 0x01) != 0;
            }

            List<GestureInfo> gestures = ReadGestures(target.Device, gestureFeature.Value);

            if (statusOnly)
            {
                PrintStatus(target.Name, rawEnabled, gestures, verbose, output);
                continue;
            }

            int changes = 0;

            // Firmware gesture mode: the touchpad itself turns two-finger
            // swipes into normal HID wheel events.
            if (rawEnabled == true && rawFeature is not null)
            {
                var disableRaw = target.Device.FeatureRequest(rawFeature.Value, 2, 0x00, 0x00, 0x00);
                if (disableRaw is { IsError: false })
                {
                    changes++;
                    if (verbose)
                        output.WriteLine($"{target.Name}: disabled raw touchpad reporting.");
                }
            }

            // Release every gesture that was diverted to host software.
            foreach (GestureInfo gesture in gestures)
            {
                if (gesture.DiversionIndex is not int diversionIndex || gesture.Diverted != true)
                    continue;

                int offset = diversionIndex >> 3;
                byte mask = (byte)(1 << (diversionIndex % 8));
                var reply = target.Device.FeatureRequest(
                    gestureFeature.Value, 4,
                    (byte)offset, 0x01, mask, 0x00);

                if (reply is { IsError: false })
                {
                    changes++;
                    if (verbose)
                        output.WriteLine($"{target.Name}: un-diverted gesture {gesture.Id} ({GestureName(gesture.Id)}).");
                }
            }

            // Make sure both two-finger wheel gestures are enabled.
            foreach (int wanted in new[] { Scroll2Finger, Scroll2FingerHorizontal, Scroll2FingerVertical })
            {
                GestureInfo? gesture = gestures.FirstOrDefault(g => g.Id == wanted);
                if (gesture?.EnableIndex is not int enableIndex || gesture.Enabled == true)
                    continue;

                int offset = enableIndex >> 3;
                byte mask = (byte)(1 << (enableIndex % 8));
                var reply = target.Device.FeatureRequest(
                    gestureFeature.Value, 2,
                    (byte)offset, 0x01, mask, mask);

                if (reply is { IsError: false })
                {
                    changes++;
                    if (verbose)
                        output.WriteLine($"{target.Name}: enabled gesture {wanted} ({GestureName(wanted)}).");
                }
            }

            List<GestureInfo> after = ReadGestures(target.Device, gestureFeature.Value);
            bool scrollStatelessDiverted = after
                .FirstOrDefault(g => g.Id == Scroll2FingerStateless)?.Diverted == true;
            bool verticalEnabled = after
                .FirstOrDefault(g => g.Id == Scroll2FingerVertical)?.Enabled == true;
            bool horizontalEnabled = after
                .FirstOrDefault(g => g.Id == Scroll2FingerHorizontal)?.Enabled != false;

            if (rawFeature is not null)
            {
                var rawAfter = target.Device.FeatureRequest(rawFeature.Value, 1, 0x00, 0x00, 0x00);
                if (rawAfter is { IsError: false, Payload.Length: >= 1 })
                    rawEnabled = (rawAfter.Value.Payload[0] & 0x01) != 0;
            }

            if (scrollStatelessDiverted || !verticalEnabled || !horizontalEnabled)
            {
                error.WriteLine($"{target.Name}: touchpad gesture restore did not stick.");
                failed = true;
                continue;
            }

            if (verbose || changes > 0)
                PrintStatus(target.Name, rawEnabled, after, verbose, output);

            if (changes == 0)
                output.WriteLine($"{target.Name}: touchpad gestures already OK.");
            else
                output.WriteLine($"{target.Name}: touchpad gestures restored.");
        }

        return failed ? 1 : 0;
    }

    private static void PrintStatus(
        string name,
        bool? rawEnabled,
        List<GestureInfo> gestures,
        bool verbose,
        TextWriter output)
    {
        GestureInfo? vertical = gestures.FirstOrDefault(g => g.Id == Scroll2FingerVertical);
        GestureInfo? horizontal = gestures.FirstOrDefault(g => g.Id == Scroll2FingerHorizontal);
        GestureInfo? stateless = gestures.FirstOrDefault(g => g.Id == Scroll2FingerStateless);

        output.WriteLine(name);
        output.WriteLine($"  Two-finger vertical scroll: {OnOff(vertical?.Enabled)}");
        output.WriteLine($"  Two-finger horizontal scroll: {OnOff(horizontal?.Enabled)}");
        output.WriteLine($"  Scroll gesture diverted to software: {(stateless is null ? "not present" : OnOff(stateless.Diverted))}");
        output.WriteLine($"  Raw touchpad reporting: {(rawEnabled is null ? "unknown" : OnOff(rawEnabled))}");

        if (verbose)
        {
            foreach (GestureInfo gesture in gestures)
            {
                output.WriteLine(
                    $"    gesture {gesture.Id,3} {GestureName(gesture.Id),-32} " +
                    $"enabled={gesture.Enabled?.ToString() ?? "?",-5} " +
                    $"diverted={gesture.Diverted?.ToString() ?? "?"}");
            }
        }
    }

    private static string OnOff(bool? value) => value switch
    {
        true => "on",
        false => "off",
        null => "unknown"
    };

    private static List<GestureInfo> ReadGestures(HidppDevice device, byte feature)
    {
        var result = new List<GestureInfo>();
        int fieldIndex = 0;
        int nextEnableIndex = 0;
        int nextDiversionIndex = 0;

        while (true)
        {
            var reply = device.FeatureRequest(feature, 0, (byte)(fieldIndex >> 8), (byte)fieldIndex);
            if (reply is null || reply.Value.IsError || reply.Value.Payload.Length < 16)
                break;

            byte[] payload = reply.Value.Payload;
            bool end = false;

            for (int i = 0; i < 8; i++)
            {
                byte high = payload[i * 2];
                byte low = payload[i * 2 + 1];

                if (high == 0x01)
                {
                    end = true;
                    break;
                }

                if ((high & 0x80) == 0)
                    continue;

                bool canBeEnabled = (high & 0x01) != 0;
                bool canBeDiverted = (high & 0x02) != 0;
                bool defaultEnabled = (high & 0x20) != 0;
                int? enableIndex = canBeEnabled || defaultEnabled ? nextEnableIndex++ : null;
                int? diversionIndex = canBeDiverted ? nextDiversionIndex++ : null;

                bool? enabled = null;
                if (enableIndex is int idx)
                {
                    int offset = idx >> 3;
                    byte mask = (byte)(1 << (idx % 8));
                    var state = device.FeatureRequest(feature, 1, (byte)offset, 0x01, mask);
                    if (state is { IsError: false, Payload.Length: >= 1 })
                        enabled = (state.Value.Payload[0] & mask) != 0;
                }

                bool? diverted = null;
                if (diversionIndex is int divIdx)
                {
                    int offset = divIdx >> 3;
                    byte mask = (byte)(1 << (divIdx % 8));
                    var state = device.FeatureRequest(feature, 3, (byte)offset, 0x01, mask);
                    if (state is { IsError: false, Payload.Length: >= 1 })
                        diverted = (state.Value.Payload[0] & mask) != 0;
                }

                result.Add(new GestureInfo(low, high, enableIndex, enabled, diversionIndex, diverted));
            }

            if (end)
                break;

            fieldIndex += 8;
        }

        return result;
    }

    private static string GestureName(int id) => id switch
    {
        1 => "tap 1 finger",
        2 => "tap 2 fingers",
        10 => "double tap 1 finger",
        20 => "track 1 finger",
        30 => "tap drag 1 finger",
        31 => "tap drag 2 fingers",
        34 => "Fn+click gesture suppression",
        41 => "scroll 2 fingers",
        42 => "scroll 2 fingers horizontal",
        43 => "scroll 2 fingers vertical",
        44 => "scroll 2 fingers stateless",
        45 => "natural scrolling",
        64 => "left edge swipe",
        65 => "right edge swipe",
        67 => "top edge swipe",
        80 => "zoom 2 fingers",
        84 => "zoom 2 fingers stateless",
        _ => "unknown"
    };
}
