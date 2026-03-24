#!/usr/bin/env python3
"""
MCP stdio-to-TCP bridge for GodotExplorer.

Reads JSON-RPC messages from stdin (Claude Code),
forwards them to the GodotExplorer TCP server (localhost:27020),
and writes responses back to stdout.

Usage in Claude Code MCP config:
{
  "mcpServers": {
    "godot-explorer": {
      "command": "python",
      "args": ["E:/Github/GodotExplorer/mcp-bridge.py"]
    }
  }
}
"""

import sys
import socket
import json
import threading
import time

HOST = "127.0.0.1"
PORT = 27020
RECONNECT_DELAY = 2
MAX_RECONNECT = 5


def connect_with_retry():
    """Connect to the GodotExplorer TCP server with retries."""
    for attempt in range(MAX_RECONNECT):
        try:
            sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            sock.connect((HOST, PORT))
            sock.settimeout(120)
            return sock
        except ConnectionRefusedError:
            if attempt < MAX_RECONNECT - 1:
                time.sleep(RECONNECT_DELAY)
            else:
                raise


def main():
    try:
        sock = connect_with_retry()
    except ConnectionRefusedError:
        err = {
            "jsonrpc": "2.0",
            "id": None,
            "error": {
                "code": -32000,
                "message": f"Cannot connect to GodotExplorer on {HOST}:{PORT}. Is the game running with the mod loaded?"
            }
        }
        sys.stdout.write(json.dumps(err) + "\n")
        sys.stdout.flush()
        sys.exit(1)

    sock_file = sock.makefile("r", encoding="utf-8")
    alive = True

    def read_responses():
        """Background thread: read TCP responses and write to stdout."""
        nonlocal alive
        try:
            while alive:
                line = sock_file.readline()
                if not line:
                    break
                sys.stdout.write(line)
                if not line.endswith("\n"):
                    sys.stdout.write("\n")
                sys.stdout.flush()
        except Exception:
            pass
        finally:
            alive = False

    reader = threading.Thread(target=read_responses, daemon=True)
    reader.start()

    # Main thread: read stdin and forward to TCP
    try:
        for line in sys.stdin:
            if not alive:
                break
            line = line.strip()
            if not line:
                continue
            try:
                sock.sendall((line + "\n").encode("utf-8"))
            except (BrokenPipeError, OSError):
                break
    except (BrokenPipeError, KeyboardInterrupt):
        pass
    finally:
        alive = False
        try:
            sock.close()
        except Exception:
            pass


if __name__ == "__main__":
    main()
