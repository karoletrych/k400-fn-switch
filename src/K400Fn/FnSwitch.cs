using K400Fn.Hid;
using K400Fn.Hidpp;

namespace K400Fn;

internal sealed class KeyboardTarget
{
    public required HidppDevice Device { get; init; }
    public required byte FeatureIndex { get; init; }
    public required string Name { get; init; }
    public required string Transport { get; init; }
}

internal sealed class KeyboardSession : IDisposable
{
    private readonly List<HidConnection> _connections = [];

    public List<KeyboardTarget> Targets { get; } = [];

    public static KeyboardSession Discover(bool verbose, TextWriter log)
    {
        var session = new KeyboardSession();
        IReadOnlyList<HidInterfaceInfo> interfaces = HidEnumerator.Enumerate();

        if (verbose)
            log.WriteLine($"Found {interfaces.Count} Logitech HID++ interface(s).");

        // Prefer the long-report collection (usage 2). It is the one that
        // carries HID++ 2.0 replies. Some older/odd devices only expose the
        // short (usage 1) collection. If a device has both, ignore its short
        // collection; otherwise process every short-only device too.
        var usable = interfaces
            .Where(i => i.Usage is 0x0001 or 0x0002 && i.OutputReportLength >= 7)
            .ToList();

        var longProductIds = usable
            .Where(i => i.Usage == 0x0002 && i.OutputReportLength >= 20)
            .Select(i => i.ProductId)
            .ToHashSet();

        IReadOnlyList<HidInterfaceInfo> candidates = usable
            .Where(i => i.Usage == 0x0002 || !longProductIds.Contains(i.ProductId))
            .OrderByDescending(i => i.OutputReportLength)
            .ToList();

        if (candidates.Count == 0)
            candidates = interfaces;

        foreach (HidInterfaceInfo info in candidates)
        {
            HidConnection? connection = HidConnection.TryOpen(info);
            if (connection is null)
            {
                if (verbose)
                    log.WriteLine($"  Cannot open {info.ProductName} ({info.ProductId:X4}).");
                continue;
            }

            session._connections.Add(connection);

            bool receiver = LooksLikeReceiver(info);
            if (verbose)
            {
                log.WriteLine(
                    $"  {info.ProductName} [046D:{info.ProductId:X4}] " +
                    $"in={info.InputReportLength} out={info.OutputReportLength} " +
                    $"{(receiver ? "receiver" : "direct")}");
            }

            byte[] indices = receiver
                ? [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]
                : [0xFF, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06];

            foreach (byte index in indices)
            {
                HidppDevice? device = HidppDevice.TryCreate(connection, index);
                if (device is null)
                    continue;

                (byte Index, ushort Id)? feature = device.FindFnInversionFeature();
                if (feature is null)
                {
                    if (verbose)
                        log.WriteLine($"    HID++ device {index}: no Fn-inversion feature.");
                    continue;
                }

                if (verbose)
                {
                    log.WriteLine(
                        $"    HID++ device {index}: Fn inversion feature {feature.Value.Id:X4} " +
                        $"at index {feature.Value.Index}.");
                }

                string name = device.GetDeviceName() ?? info.ProductName;
                if (string.IsNullOrWhiteSpace(name))
                    name = "Logitech keyboard";

                string transport = receiver
                    ? $"{(info.ProductName.Length > 0 ? info.ProductName : "receiver")} " +
                      $"(046D:{info.ProductId:X4}), device {index}"
                    : "direct / Bluetooth";

                session.Targets.Add(new KeyboardTarget
                {
                    Device = device,
                    FeatureIndex = feature.Value.Index,
                    Name = name,
                    Transport = transport
                });

                // One keyboard per receiver is the normal case. Stop scanning
                // as soon as we have a usable device instead of waiting out
                // the timeouts for every empty pairing slot.
                break;
            }
        }

        return session;
    }

    private static bool LooksLikeReceiver(HidInterfaceInfo info)
    {
        if (info.ProductName.Contains("receiver", StringComparison.OrdinalIgnoreCase))
            return true;

        return info.ProductId is
            0xC52B or // Unifying
            0xC532 or // Unifying
            0xC534 or // Unifying
            0xC537 or // Unifying
            0xC539 or // Unifying / Lightspeed
            0xC53F or // Unifying
            0xC541 or // Lightspeed
            0xC542 or // Logi Bolt
            0xC545 or // Lightspeed
            0xC547 or // Logi Bolt
            0xC548 or // Logi Bolt
            0xC549;   // Logi Bolt
    }

    public void Dispose()
    {
        foreach (HidConnection connection in _connections)
            connection.Dispose();
    }
}

internal static class FnSwitcher
{
    public static int Run(
        FnMode? targetMode,
        bool toggle,
        bool verbose,
        TextWriter output,
        TextWriter error)
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
            FnState? before = target.Device.ReadFnState(target.FeatureIndex);

            if (targetMode is null && !toggle)
            {
                // status
                if (before is null)
                {
                    error.WriteLine($"{target.Name}: could not read the current mode.");
                    failed = true;
                    continue;
                }

                output.WriteLine($"{target.Name} ({target.Transport})");
                output.WriteLine($"  Primary: {before.Value.ModeName}");
                if (before.Value.Default is not null)
                    output.WriteLine($"  Factory default: {before.Value.DefaultName}");
                continue;
            }

            FnMode desired;
            if (toggle)
            {
                if (before is null)
                {
                    error.WriteLine($"{target.Name}: could not read the current mode, so it cannot be toggled.");
                    failed = true;
                    continue;
                }

                desired = before.Value.Mode == FnMode.FunctionKeys
                    ? FnMode.MediaKeys
                    : FnMode.FunctionKeys;
            }
            else
            {
                desired = targetMode!.Value;
            }

            string desiredName = desired == FnMode.FunctionKeys ? "F-keys" : "media keys";

            if (before?.Mode == desired)
            {
                output.WriteLine($"{target.Name}: already using {desiredName}.");
                continue;
            }

            bool ok = target.Device.SetFnState(target.FeatureIndex, desired);
            FnState? after = target.Device.ReadFnState(target.FeatureIndex);

            bool changed = after?.Mode == desired || (after is null && ok);
            if (!changed)
            {
                error.WriteLine($"{target.Name}: failed to switch the F-key mode.");
                failed = true;
                continue;
            }

            output.WriteLine($"{target.Name}: {desiredName} enabled.");
        }

        return failed ? 1 : 0;
    }
}
