"""
send_emote.py — example integration script for sending emotes to RumiVtsController over TCP.

Copy this file into your project and adapt it to trigger emotes from whatever
system you're integrating (LLM, chat bot, hotkey, etc.). Set HOST to the IP or
hostname of the machine running RumiVtsController and PORT to match
config.json > expression.port (default 5100).

Usage:
    python send_emote.py <emote_name> [duration_seconds]

Examples:
    python send_emote.py blush
    python send_emote.py angry 2.5
    python send_emote.py smile

Protocol:
    Send a newline-terminated string over TCP. Accepts a plain action name or
    a JSON object with "action" and optional "durationSeconds".

Built-in actions (handled natively):
    blink, winkleft, winkright, smile, halfsmile
    sleep/afk, wake/wakeup/awake, dizzy, stopdizzy

Expression names come from the "expressions" list on each hotkey in config.json.
"""

import sys
import json
import socket

HOST = "127.0.0.1"  # IP or hostname of the PC running RumiVtsController
PORT = 5100


def send_emote(action: str, duration: float | None = None) -> None:
    if duration is not None:
        payload = json.dumps({"action": action, "durationSeconds": duration})
    else:
        payload = action  # plain string — simplest form

    data = (payload + "\n").encode("utf-8")

    try:
        with socket.create_connection((HOST, PORT), timeout=5) as sock:
            sock.sendall(data)
        print(f"[OK] Sent: {payload!r}")
    except ConnectionRefusedError:
        print(f"[ERROR] Connection refused at {HOST}:{PORT}")
        print(f"  - Is RumiVtsController running?")
        print(f"  - Is expression.enabled = true in config.json?")
        print(f"  - Is {HOST} correct and reachable? (try ping {HOST})")
        print(f"  - Is port {PORT} allowed through the firewall on the host machine?")
        sys.exit(1)
    except TimeoutError:
        print(f"[ERROR] Connection timed out to {HOST}:{PORT}")
        print(f"  - Host may be unreachable or port {PORT} is blocked by a firewall.")
        sys.exit(1)
    except OSError as e:
        print(f"[ERROR] {e}")
        sys.exit(1)


def print_help() -> None:
    print(__doc__)


if __name__ == "__main__":
    args = sys.argv[1:]
    if not args or args[0] in ("-h", "--help"):
        print_help()
        sys.exit(0)

    emote = args[0]
    duration = None
    if len(args) >= 2:
        try:
            duration = float(args[1])
        except ValueError:
            print(f"[ERROR] Invalid duration: {args[1]!r} (must be a number)")
            sys.exit(1)

    send_emote(emote, duration)
