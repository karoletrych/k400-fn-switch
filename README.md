# K400 Fn Switch

A tiny tool that switches the Logitech K400 Plus F-key row between standard
**F1-F12** keys and **media/special** keys — without Logi Options+.

- **Windows:** self-contained `K400Fn.exe` (no .NET runtime needed).
- **Linux:** small bash script using `/dev/hidraw` (see [Linux](#linux)).

**Windows:** no installation, no background service, no admin rights, no network
access. The setting is written to the keyboard itself. On some K400 Plus
firmware it is forgotten when the keyboard sleeps or is powered off, in which
case just run the tool again (the included shortcut makes that a double-click).

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
K400Fn fkeys --quiet   Silent, for Task Scheduler
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

For keyboards that forget the setting after sleeping, the zip also includes:

- `Auto F-keys.cmd` — installs a scheduled task that re-applies F-keys every
  15 minutes while you are logged on.
- `Remove auto F-keys.cmd` — removes that scheduled task.

The scheduled task forces F-keys. Remove it if you want to keep media keys
selected for a while.

## Linux

The Linux version is a single bash script that talks to `/dev/hidraw` directly.
It uses the same HID++ command as the Windows build, discovers the feature and
device index, and falls back to the known K400 Plus values (feature 9,
device 1) if discovery does not answer.

### Install

Grab `k400-fn-switch-linux.tar.gz` from the release, or download the three
files from the `linux/` directory:

```bash
tar -xzf k400-fn-switch-linux.tar.gz
cd linux
sudo bash install.sh
k400-fn fkeys
k400-fn media
k400-fn status
```

The installer copies the script to `/usr/local/bin/k400-fn` and installs a udev
rule that lets your logged-in user access Logitech hidraw devices. Without the
udev rule, run the script with `sudo`.

Run it without installing:

```bash
bash k400-fn.sh fkeys
```

(If you prefer `./k400-fn.sh`, run `chmod +x k400-fn.sh` first.)

Useful commands:

```bash
k400-fn fkeys --dry-run     # show the node and bytes, do not write
k400-fn --help
```

To re-apply F-keys after the keyboard sleeps, add a cron entry:

```cron
*/15 * * * * /usr/local/bin/k400-fn fkeys --quiet
```

**Linux status:** the script is tested at the protocol level with a simulated
hidraw endpoint, including discovery, read, set and verification. It has not
yet been tested on a physical Linux machine in this repository. The command is
the same one used by the long-standing Solaar/udev-rule approach. Feedback and
patches from Linux users are welcome.

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

### The K400 Plus sleep quirk

Some K400 Plus firmware resets the Fn inversion setting after a long period of
inactivity or when the keyboard is switched off. This is a keyboard firmware
behavior, not something the tool can permanently override.

If your keyboard forgets the setting, run the tool again (`K400Fn.exe` on
Windows, `k400-fn fkeys` on Linux). The Windows release zip includes
`Use F-keys.cmd`, which you can pin to the taskbar or Start menu. You can also
schedule `K400Fn.exe fkeys --quiet` with Task Scheduler (or use the included
`Auto F-keys.cmd`).

The setting is still a real device setting: it is not lost when you unplug the
receiver, close the tool, or reboot Windows. It only resets when the keyboard
firmware decides to reset it.

## How it works

The keyboard is a HID++ 2.0 device. The tool:

1. Finds the Logitech HID++ interface (the vendor collection with usage page
   `0xFF00` on Windows; a `/dev/hidraw` node on Linux).
2. Finds the K400 Plus behind the Unifying/Bolt receiver.
3. Looks up the `NEW_FN_INVERSION` feature (`0x40A2`) or the older
   `FN_INVERSION` feature (`0x40A0`).
4. Reads or writes the one-byte mode:
   - `0` = F1-F12 are primary
   - `1` = media/special keys are primary

The protocol details and example packets are in
[docs/protocol.md](docs/protocol.md).

The value is stored by the keyboard. The tool does not need to run in the
background, although some K400 Plus firmware resets the value after a long idle
period (see above).

## Build from source

Requirements: Windows 10/11 x64 and the .NET 10 SDK (only for building; the
release executable is self-contained).

```powershell
dotnet build .\src\K400Fn\K400Fn.csproj
dotnet run --project .\src\K400Fn\K400Fn.csproj -- status
```

Or build the release files:

```powershell
.\build.ps1
```

The result is `artifacts\k400-fn-switch-win-x64.zip` and
`artifacts\k400-fn-switch-linux.tar.gz`. The Linux script itself needs no
build; run it from `linux/`.

## Troubleshooting

**"No supported Logitech keyboard found."**

- Turn the keyboard on and press a key to wake it.
- Make sure the Unifying/Bolt receiver is plugged in.
- Quit Logi Options+ / Logitech G HUB and try again; they may be holding the
  HID++ collection open.
- Try a different USB port.

**The tool worked, but the mode changed back later.**

- Some K400 Plus firmware resets the setting after a long idle period or when
  the keyboard is turned off. Just run the tool again, or pin `Use F-keys.cmd`
  somewhere convenient.
- Logi Options+ or another Logitech program may also be re-applying its own
  saved setting. Quit it, set the mode, and try again.

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
