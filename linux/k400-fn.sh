#!/usr/bin/env bash
#
# k400-fn.sh - switch the Logitech K400 Plus F-key row on Linux.
#
# Talks HID++ 2.0 to the keyboard through /dev/hidraw. No Logi Options+,
# no background service, no extra packages.
#
# The K400 Plus exposes the NEW_FN_INVERSION feature (0x40A2). The script
# discovers the feature index and device index first, then falls back to the
# known K400 Plus values (feature 9, device 1) if discovery does not answer.
#
# Permission: /dev/hidraw* is root-only by default. Either run this with sudo
# or install the included udev rule (70-k400-fn.rules) so your logged-in user
# can access Logitech hidraw devices.

set -u
export LC_ALL=C

PROG=${0##*/}
TIMEOUT=${K400_TIMEOUT:-2}
DEVICE_INDEX=${K400_DEVICE:-}
FEATURE_INDEX=${K400_FEATURE:-}
QUIET=0
DRY_RUN=0
MODE=""

usage() {
    cat <<EOF
Usage: $PROG [command] [options]

Commands:
  fkeys       Make F1-F12 the primary keys (default)
  media       Make the media/special keys primary
  status      Show the current mode

Options:
  -d, --device N    Device index (1-6, or 255 for a direct device)
  -f, --feature N   Fn-inversion feature index (normally discovered)
  -t, --timeout S   Reply timeout in seconds (default: $TIMEOUT)
  -q, --quiet       No output on success
  -n, --dry-run     Show what would be written, do not touch the device
  -h, --help        Show this help

Examples:
  sudo $PROG fkeys
  sudo $PROG media
  sudo $PROG status
  $PROG fkeys --dry-run

The setting is stored by the keyboard. Some K400 Plus firmware resets it after
a long idle period; run the command again if that happens. A cron entry such
as "*/15 * * * * /usr/local/bin/k400-fn fkeys --quiet" can re-apply it.
EOF
}

say() {
    if [ "$QUIET" -eq 0 ]; then
        printf '%s\n' "$*"
    fi
}

err() {
    printf '%s\n' "$*" >&2
}

# Convert 0x09 / 09 / 9 to "09".
normalize_hex() {
    local value=$1
    value=${value#0x}
    value=${value#0X}
    if ! [[ "$value" =~ ^[0-9a-fA-F]{1,2}$ ]]; then
        return 1
    fi
    printf '%02x' "$((16#$value))"
}

# Find hidraw nodes that belong to a Logitech receiver or keyboard.
candidate_nodes() {
    local sys uevent name
    local root=${K400_SYSFS:-/sys/class/hidraw}
    local dev_prefix=${K400_DEV_PREFIX:-/dev}
    for sys in "$root"/hidraw*; do
        [ -e "$sys" ] || continue
        uevent="$sys/device/uevent"
        [ -r "$uevent" ] || continue
        grep -qiE '^HID_ID=.*046d' "$uevent" || continue
        name=$(sed -n 's/^HID_NAME=//p' "$uevent")
        case "$name" in
            *[Rr]eceiver*|*[Kk]eyboard*|*K400*|*k400*) ;;
            *) continue ;;
        esac
        printf '%s/%s\n' "$dev_prefix" "${sys##*/}"
    done
}

first_writable_node() {
    local node
    while IFS= read -r node; do
        if [ -w "$node" ]; then
            printf '%s\n' "$node"
            return 0
        fi
    done < <(candidate_nodes)
    return 1
}

# Write a short HID++ report:
#   10 <device> <feature> <function<<4 | software-id> <p0> <p1> <p2>
# The binary report is produced directly by printf; it must never pass through
# a shell variable because variables cannot hold NUL bytes.
send_report() {
    local node=$1
    local device=$2
    local feature=$3
    local function=$4
    local p0=$5
    local p1=$6
    local p2=$7
    local request=$(((function << 4) | 0x0B))

    # The feature index may arrive as "0", "9" or "09". Normalize it to two
    # lowercase hex digits. Passing "09" through %x would make printf treat it
    # as an invalid octal number, so insert it as a string after normalizing.
    if ! [[ "$feature" =~ ^[0-9a-fA-F]{1,2}$ ]]; then
        return 1
    fi
    feature=$(printf '%02x' "$((16#$feature))")

    if [ "$DRY_RUN" -eq 1 ]; then
        printf '  %s <- 10 %02x %s %02x %02x %02x %02x\n' \
            "$node" "$device" "$feature" "$request" "$p0" "$p1" "$p2"
        return 0
    fi

    printf '%b' "$(printf '\\x10\\x%02x\\x%s\\x%02x\\x%02x\\x%02x\\x%02x' \
        "$device" "$feature" "$request" "$p0" "$p1" "$p2")" >"$node" 2>/dev/null
}

# Read one HID++ report and return it as lowercase hex without separators.
# hidraw returns one full report per read(); a 32-byte buffer accepts both the
# 7-byte short and 20-byte long report shapes.
read_report() {
    local node=$1
    timeout "$TIMEOUT" dd if="$node" bs=32 count=1 2>/dev/null \
        | od -An -v -tx1 \
        | tr -d ' \n'
}

# Drop one pending report (for example the acknowledgement of a set command).
drain_report() {
    local node=$1
    timeout 0.4 dd if="$node" bs=32 count=1 of=/dev/null 2>/dev/null || true
}

# Find a node/device/feature triple that answers getFeature(0x40A2).
# Prints: <node> <device-index> <feature-index>
discover() {
    local node idx feature reply expected
    while IFS= read -r node; do
        [ -w "$node" ] || continue
        for idx in 1 2 3 4 5 6 255; do
            send_report "$node" "$idx" 0 0 0x40 0xA2 0 || continue
            reply=$(read_report "$node")
            [ "${#reply}" -ge 10 ] || continue
            [ "${reply:0:2}" = "10" ] || continue

            if [ "$idx" -eq 255 ]; then
                case "${reply:2:2}" in
                    00|ff) ;;
                    *) continue ;;
                esac
            else
                expected=$(printf '%02x' "$idx")
                [ "${reply:2:2}" = "$expected" ] || continue
            fi

            [ "${reply:4:2}" = "00" ] || continue
            [ "${reply:6:2}" = "0b" ] || continue

            feature="${reply:8:2}"
            case "$feature" in
                ""|00|ff) continue ;;
            esac

            printf '%s %s %s\n' "$node" "$idx" "$feature"
            return 0
        done
    done < <(candidate_nodes)
    return 1
}

# Read the current state. Prints 00 (F-keys) or 01 (media keys).
read_state() {
    local node=$1
    local idx=$2
    local feature=$3
    local reply expected

    send_report "$node" "$idx" "$feature" 0 0 0 0 || return 1
    reply=$(read_report "$node")
    [ "${#reply}" -ge 10 ] || return 1
    [ "${reply:0:2}" = "10" ] || return 1

    if [ "$idx" -eq 255 ]; then
        case "${reply:2:2}" in
            00|ff) ;;
            *) return 1 ;;
        esac
    else
        expected=$(printf '%02x' "$idx")
        [ "${reply:2:2}" = "$expected" ] || return 1
    fi

    [ "${reply:4:2}" = "$feature" ] || return 1
    [ "${reply:6:2}" = "0b" ] || return 1
    printf '%s\n' "${reply:8:2}"
}

# Set the state (00 = F-keys, 01 = media keys).
set_state() {
    local node=$1
    local idx=$2
    local feature=$3
    local state=$4
    send_report "$node" "$idx" "$feature" 1 "$state" 0 0
}

# --- parse arguments -------------------------------------------------------

if [ $# -eq 0 ]; then
    MODE=fkeys
fi

while [ $# -gt 0 ]; do
    case "$1" in
        fkeys|fn|function|functionkeys)
            MODE=fkeys
            ;;
        media|special|mediakeys)
            MODE=media
            ;;
        status|show)
            MODE=status
            ;;
        -d|--device)
            [ $# -ge 2 ] || { err "Missing value for $1"; exit 2; }
            DEVICE_INDEX=$2
            shift
            ;;
        -f|--feature)
            [ $# -ge 2 ] || { err "Missing value for $1"; exit 2; }
            FEATURE_INDEX=$2
            shift
            ;;
        -t|--timeout)
            [ $# -ge 2 ] || { err "Missing value for $1"; exit 2; }
            TIMEOUT=$2
            shift
            ;;
        -q|--quiet)
            QUIET=1
            ;;
        -n|--dry-run)
            DRY_RUN=1
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            err "Unknown option: $1"
            usage >&2
            exit 2
            ;;
    esac
    shift
done

[ -n "$MODE" ] || MODE=fkeys

case "$MODE" in
    fkeys) STATE=00; MODE_NAME="F-keys" ;;
    media) STATE=01; MODE_NAME="media keys" ;;
    status) STATE=""; MODE_NAME="" ;;
esac

if [ -n "$DEVICE_INDEX" ]; then
    case "$DEVICE_INDEX" in
        0xff|0xFF|255) DEVICE_INDEX=255 ;;
        1|2|3|4|5|6) ;;
        *) err "Device index must be 1-6 or 255."; exit 2 ;;
    esac
fi

if [ -n "$FEATURE_INDEX" ]; then
    if ! FEATURE_INDEX=$(normalize_hex "$FEATURE_INDEX"); then
        err "Feature index must be one or two hex digits, for example 9 or 0x09."
        exit 2
    fi
fi

if ! command -v timeout >/dev/null 2>&1; then
    err "The 'timeout' command is required (coreutils)."
    exit 1
fi

if ! command -v od >/dev/null 2>&1; then
    err "The 'od' command is required (coreutils)."
    exit 1
fi

# --- find the keyboard -----------------------------------------------------

NODE=""

if [ -n "$DEVICE_INDEX" ] && [ -n "$FEATURE_INDEX" ]; then
    NODE=$(first_writable_node) || {
        err "No writable Logitech receiver/keyboard hidraw device found."
        err "Plug in the receiver, turn the keyboard on, and check permissions."
        exit 1
    }
elif discovered=$(discover); then
    read -r NODE DEVICE_INDEX FEATURE_INDEX <<<"$discovered"
    say "Found Logitech keyboard on $NODE (device $DEVICE_INDEX, feature 0x$FEATURE_INDEX)."
else
    # Discovery did not answer. Fall back to the known K400 Plus values.
    DEVICE_INDEX=${DEVICE_INDEX:-1}
    FEATURE_INDEX=${FEATURE_INDEX:-09}
    NODE=$(first_writable_node) || {
        err "No writable Logitech receiver/keyboard hidraw device found."
        err "Plug in the receiver, turn the keyboard on, and check permissions."
        exit 1
    }
    say "Could not auto-detect the feature; using device $DEVICE_INDEX, feature 0x$FEATURE_INDEX."
fi

if [ "$DRY_RUN" -eq 1 ]; then
    say "Dry run:"
    if [ "$MODE" = "status" ]; then
        send_report "$NODE" "$DEVICE_INDEX" "$FEATURE_INDEX" 0 0 0 0
    else
        send_report "$NODE" "$DEVICE_INDEX" "$FEATURE_INDEX" 1 "$STATE" 0 0
    fi
    exit 0
fi

# --- do the thing ----------------------------------------------------------

if [ "$MODE" = "status" ]; then
    state=$(read_state "$NODE" "$DEVICE_INDEX" "$FEATURE_INDEX") || {
        err "Could not read the current mode."
        exit 1
    }
    case "$state" in
        00) say "F-keys are primary." ;;
        01) say "Media keys are primary." ;;
        *) err "Unknown state byte: 0x$state"; exit 1 ;;
    esac
    exit 0
fi

set_state "$NODE" "$DEVICE_INDEX" "$FEATURE_INDEX" "$STATE" || {
    err "Failed to write to $NODE."
    err "If this is a permission error, install 70-k400-fn.rules or use sudo."
    exit 1
}

# Best-effort verification: drain the set acknowledgement, then read it back.
sleep 0.2
drain_report "$NODE"
current=$(read_state "$NODE" "$DEVICE_INDEX" "$FEATURE_INDEX" 2>/dev/null) || current=""
if [ "$current" = "$STATE" ]; then
    say "$MODE_NAME enabled."
else
    say "$MODE_NAME command sent (could not verify the new state)."
fi
