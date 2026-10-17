# Protocol notes

These are the HID++ 2.0 details used by K400 Fn Switch. They were verified
against a Logitech K400 Plus (WPID `404D`) connected through a Unifying
receiver (`046D:C52B`) on Windows.

## Transport

A Logitech receiver exposes several HID collections. The relevant vendor
collections use usage page `0xFF00`:

| Collection | Usage | Report size | Use |
| --- | --- | --- | --- |
| `MI_02&COL01` | `0xFF00/0x0001` | 7 bytes in/out | short HID++ reports |
| `MI_02&COL02` | `0xFF00/0x0002` | 20 bytes in/out | long HID++ reports |
| `MI_02&COL03` | `0xFF00/0x0004` | 32 bytes in/out | not used here |

On the tested receiver, requests sent on the short collection were accepted,
but HID++ 2.0 replies came back on the long collection. The tool therefore
prefers the `0xFF00/0x0002` collection and uses 20-byte long reports.

The long report is:

```text
offset  size  meaning
0       1     report ID: 0x11
1       1     device index (1-6 for a receiver, 0xFF for a direct device)
2       1     feature index
3       1     function << 4 | software ID
4       16    parameters, zero padded
```

The short report is the same idea with report ID `0x10`, a 5-byte payload, and
`function << 4 | software ID` at offset 3.

The software ID identifies the client. This tool uses `0x0B` (the value Solaar
uses). The device echoes the feature index and request ID in its reply.

## Finding a device

A Unifying/Bolt receiver addresses its paired devices with indices `1` through
`6`. A directly connected or Bluetooth device is addressed as `0xFF` (some
Bluetooth devices reply with index `0x00`).

The tool pings each candidate with the root feature (`0x0000`), function 1
(`getProtocolVersion`):

```text
request:  11 <dev> 00 1B 00 00 <random> ...
response: 11 <dev> 00 1B <major> <minor> <random> ...
```

A reply means the device is present and awake.

## Feature discovery

Root feature `0x0000`, function 0 (`getFeature`), takes a 2-byte feature ID:

```text
request:  11 01 00 0B 40 A2 00 ...
response: 11 01 00 0B 09 00 ...
                          ^^ feature index (9 in this example)
```

The K400 Plus advertises:

- `0x40A2` — `NEW_FN_INVERSION` (newer, includes a factory-default byte)
- and not the older `0x40A0` on the firmware tested

The tool tries `0x40A2` first and falls back to `0x40A0`.

## Fn inversion

Both features use the same function numbering:

- Function 0 (`0x0B` request ID): read the state
- Function 1 (`0x1B` request ID): write the state

For `0x40A2`, the response payload is:

| Byte | Meaning |
| --- | --- |
| 0 | current state |
| 1 | factory default state |

State values:

| Value | Meaning |
| --- | --- |
| `0x00` | F1-F12 are the primary keys |
| `0x01` | media/special keys are the primary keys |

### Read

```text
request:  11 01 09 0B 00 00 00 ...
response: 11 01 09 0B 00 01 ...
                       ^^ current state = 0 (F-keys)
                          ^^ factory default = 1 (media keys)
```

### Set F-keys

```text
request:  11 01 09 19 00 00 00 ...
response: 11 01 09 19 00 01 ...
```

### Set media keys

```text
request:  11 01 09 19 01 00 00 ...
response: 11 01 09 19 01 01 ...
```

The device stores the new state in its own memory, not in the host operating
system. Closing the tool, unplugging the receiver, or rebooting Windows does not
change it.

Some K400 Plus firmware nevertheless resets the value after a long idle period
or when the keyboard is switched off. This has been reported by multiple users
and is a keyboard firmware behavior; re-send the command after the keyboard
wakes if needed.

## Error replies

HID++ 2.0 errors have the shape:

```text
11 <dev> FF <feature> <request-id> <error>
```

HID++ 1.0 errors use `0x8F` instead of `0xFF`. Examples seen while scanning
empty receiver slots:

```text
10 02 8F 00 0B 09 00   # device 2: UNKNOWN_DEVICE
10 FF 8F 00 0B 01 00   # receiver itself does not implement getFeature
```

## Notes

- The older short-report command used by `h8man/k400p-fn-lock-win`
  (`10 01 09 19 00 00 00`) also works on this keyboard. That command hard-codes
  feature index 9 and device index 1.
- This tool discovers the feature index and device index instead, which makes
  it less likely to write the wrong thing after a firmware or receiver change.
- A hardware `Fn+Esc` lock does not exist on the K400 Plus; the setting is
  controlled through this HID++ feature.
