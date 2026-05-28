#!/usr/bin/env python3
"""Fake gpu-screen-recorder for replay-buffer-mode tests.

In real replay mode, `-o` is a *directory* and each SIGUSR1 flush produces a
new file inside it. This fake mirrors that: it parses `-o`, on SIGUSR1 it
writes a uniquely-named .mp4 to the directory and stays running, and on
SIGINT it exits cleanly. Implemented in Python for the same SIG_IGN-
restoration reasons as fake-gpu-screen-recorder.sh.
"""
import os
import signal
import sys
import time

output_dir = ""
args = sys.argv[1:]
i = 0
while i < len(args):
    if args[i] == "-o" and i + 1 < len(args):
        output_dir = args[i + 1]
        i += 2
    else:
        i += 1


_save_counter = 0


def save_replay():
    global _save_counter
    _save_counter += 1
    ts = time.strftime("%Y-%m-%d_%H-%M-%S")
    name = f"Replay_{ts}_{_save_counter:03d}.mp4"
    path = os.path.join(output_dir, name)
    # Write in two stages with a tiny pause so PreRollRecorder's
    # size-stability poll has something to settle on. The contents are
    # arbitrary; tests only care that the file exists and stops growing.
    with open(path, "wb") as fh:
        fh.write(b"fake-preroll-part-1")
        fh.flush()
        os.fsync(fh.fileno())
    time.sleep(0.05)
    with open(path, "ab") as fh:
        fh.write(b"-and-part-2")
        fh.flush()
        os.fsync(fh.fileno())


def on_sigint(signum, frame):
    print("GOT_SIGINT", flush=True)
    sys.exit(0)


def on_sigusr1(signum, frame):
    print("GOT_SIGUSR1", flush=True)
    save_replay()


signal.signal(signal.SIGINT, on_sigint)
signal.signal(signal.SIGUSR1, on_sigusr1)

if output_dir:
    os.makedirs(output_dir, exist_ok=True)

print(f"STARTED PID={os.getpid()}", flush=True)
while True:
    time.sleep(0.5)
