#!/usr/bin/env bash
#
# Install k400-fn and its udev rule. Run with sudo.

set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "Run this with sudo: sudo $0" >&2
    exit 1
fi

DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)

if [ "${1:-}" = "--uninstall" ]; then
    rm -f /usr/local/bin/k400-fn
    rm -f /etc/udev/rules.d/70-k400-fn.rules
    if command -v udevadm >/dev/null 2>&1; then
        udevadm control --reload-rules || true
        udevadm trigger || true
    fi
    echo "Removed k400-fn."
    exit 0
fi

install -m 0755 "$DIR/k400-fn.sh" /usr/local/bin/k400-fn
install -m 0644 "$DIR/70-k400-fn.rules" /etc/udev/rules.d/70-k400-fn.rules

if command -v udevadm >/dev/null 2>&1; then
    udevadm control --reload-rules || true
    udevadm trigger || true
fi

echo "Installed /usr/local/bin/k400-fn"
echo "Try: k400-fn fkeys"
echo
echo "If access is still denied, log out and back in (or run k400-fn with sudo)."
