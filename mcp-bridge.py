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
import os
import socket
import json
import threading
import time

HOST = "127.0.0.1"
PORT = 27020


def connect():
    """Connect to the GodotExplorer TCP server."""
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_KEEPALIVE, 1)
    sock.connect((HOST, PORT))
    # No timeout on the socket — we want blocking reads that wait forever
    sock.settimeout(None)
    return sock


def connect_with_retry(max_attempts=10, delay=2):
    """Connect with retries for game startup."""
    for attempt in range(max_attempts):
        try:
            return connect()
        except (ConnectionRefusedError, OSError):
            if attempt < max_attempts - 1:
                time.sleep(delay)
    raise ConnectionRefusedError(f"Cannot connect after {max_attempts} attempts")


def main():
    # Connect to game
    try:
        sock = connect_with_retry()
    except ConnectionRefusedError:
        err = {
            "jsonrpc": "2.0",
            "id": None,
            "error": {
                "code": -32000,
                "message": f"Cannot connect to GodotExplorer on {HOST}:{PORT}. "
                           "Is the game running with the mod loaded?"
            }
        }
        sys.stdout.write(json.dumps(err) + "\n")
        sys.stdout.flush()
        sys.exit(1)

    # Use unbuffered binary IO for stdin to avoid Python's line buffering issues
    stdin_bin = sys.stdin.buffer
    sock_lock = threading.Lock()
    alive = True

    def read_responses():
        """Background thread: read newline-delimited JSON from TCP, write to stdout."""
        nonlocal alive
        buf = b""
        try:
            while alive:
                chunk = sock.recv(65536)
                if not chunk:
                    break  # Server closed connection
                buf += chunk
                # Process complete lines
                while b"\n" in buf:
                    line, buf = buf.split(b"\n", 1)
                    line = line.strip()
                    if line:
                        sys.stdout.buffer.write(line + b"\n")
                        sys.stdout.buffer.flush()
        except (OSError, ConnectionError):
            pass
        finally:
            alive = False

    def read_stdin():
        """Main thread: read newline-delimited JSON from stdin, forward to TCP."""
        nonlocal alive
        buf = b""
        try:
            while alive:
                chunk = stdin_bin.read(1)
                if not chunk:
                    break  # stdin closed (Claude Code shut down)
                buf += chunk
                if chunk == b"\n":
                    line = buf.strip()
                    buf = b""
                    if line:
                        with sock_lock:
                            try:
                                sock.sendall(line + b"\n")
                            except (BrokenPipeError, OSError):
                                break
        except (OSError, KeyboardInterrupt):
            pass
        finally:
            alive = False

    # Start response reader as daemon thread
    reader = threading.Thread(target=read_responses, daemon=True)
    reader.start()

    # Run stdin reader on main thread (blocks until stdin closes)
    read_stdin()

    # Cleanup
    try:
        sock.close()
    except Exception:
        pass


if __name__ == "__main__":
    main()
