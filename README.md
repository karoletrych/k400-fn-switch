# K400 Fn Switch

A tiny Windows command-line tool that switches the Logitech K400 Plus F-key row
between standard **F1-F12** keys and **media/special** keys — without Logi
Options+.

**No installation. No background process. No admin rights. No network access.**
The setting is written to the keyboard's own memory, so it survives reboots.

[Download the latest release](https://github.com/karoletrych/k400-fn-switch/releases/latest)

## Why

The K400 Plus defaults to media keys on the top row. Logitech's only documented
way to make F1-F12 the primary keys is Logi Options+, which is a large,
always-running application for a single checkbox. This tool talks to the
keyboard's HID++ interface directly.

If Logi Options+ is broken or will not start, this still works.

## Usage

```text
K400Fn                 Make F1-F12 the primary keys (the default)
K400Fn fkeys           Same as above
K400Fn media           Make the media/special keys primary
K400Fn toggle          Flip the current mode
K400Fn status          Show the current mode
K400Fn status -v       Also show HID discovery details
K400Fn --help          Show help
```

Examples:

```powershell
# I use function keys almost all the time:
.\K400Fn.exe

# Once in a while I want the media keys back:
.\K400Fn.exe media

# Check what the keyboard is doing right now:
.\K400Fn.exe status
```

The release zip also contains three double-click helpers:

- `Use F-keys.cmd`
- `Use media keys.cmd`
- `Show status.cmd`

Each one runs the tool and waits for a key press so the window does not vanish.

## Supported hardware

Tested with:

- **Logitech Wireless Touch Keyboard K400 Plus** (WPID `404D`)
- Connected through a **Unifying receiver** (`046D:C52B`)

The tool discovers the Fn-inversion feature through HID++ 2.0, so other
Logitech keyboards that expose feature `0x40A2` (new Fn inversion) or `0x40A0`
(legacy Fn inversion) should also work. Feedback and pull requests for other
models are welcome.

The K400 Plus does not have the `Fn+Esc` hardware lock found on some other
Logitech keyboards. Logitech's documented method for this model is their
software, which is what this project replaces.

## How it works

The keyboard is a HID++ 2.0 device. The tool:

1. Finds the Logitech vendor HID collection (usage page `0xFF00`).
2. Finds the K400 Plus behind the Unifying/Bolt receiver.
3. Looks up the `NEW_FN_INVERSION` feature (`0x40A2`) or the older
   `FN_INVERSION` feature (`0x40A0`).
4. Reads or writes the one-byte mode:
   - `0` = F1-F12 are primary
   - `1` = media/special keys are primary

The protocol details and example packets are in
[docs/protocol.md](docs/protocol.md).

The value is stored in the keyboard itself (HID++ feature `0x40A2` is
non-volatile), so the tool does not need to run at startup.

## Build from source

Requirements: Windows 10/11 x64 and the .NET 10 SDK (only for building; the
release executable is self-contained).

```powershell
dotnet build .\src\K400Fn\K400Fn.csproj
dotnet run --project .\src\K400Fn\K400Fn.csproj -- status
```

Or build the release zip:

```powershell
.\build.ps1
```

The result is `artifacts\k400-fn-switch-win-x64.zip`.

## Troubleshooting

**"No supported Logitech keyboard found."**

- Turn the keyboard on and press a key to wake it.
- Make sure the Unifying/Bolt receiver is plugged in.
- Quit Logi Options+ / Logitech G HUB and try again; they may be holding the
  HID++ collection open.
- Try a different USB port.

**The tool worked, but the mode changed back later.**

- Logi Options+ or another Logitech program may be re-applying its own saved
  setting. Quit it, set the mode, and try again.

**I want to remove it completely.**

- Delete the executable. The keyboard keeps the last mode. Before deleting,
  run `K400Fn.exe media` if you want to restore the factory default.

## Credits

Protocol information and prior art:

- [Logitech HID++ 2.0 documentation](https://github.com/Logitech/cpg-docs/tree/master/hidpp20)
- [Solaar](https://github.com/pwr-Solaar/Solaar) — the reference open-source
  implementation of Logitech HID++ on Linux
- [OpenLogi HID++ feature docs](https://openlogi.org/hidpp/features/x40a2-fn-inversion)
- [h8man/k400p-fn-lock-win](https://github.com/h8man/k400p-fn-lock-win) — an
  earlier K400+ Windows tool that demonstrated the short-report command

This project is not affiliated with Logitech. Logitech, Unifying, Bolt, and
K400 are trademarks of Logitech Europe S.A. or its affiliates.

## License

MIT
