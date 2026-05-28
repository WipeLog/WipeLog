#!/usr/bin/env python3
"""Fake gpu-screen-recorder for tests.

Parses the -o argument to find the output path, traps SIGINT to write a
marker file and exit cleanly, and traps SIGUSR1 to write the marker without
exiting (replay-buffer save). Echoes received signals to stdout so tests can
observe behavior. Implemented in Python rather than bash because POSIX
forbids restoring SIG_IGN dispositions inherited at shell startup, which
breaks `trap INT` for some launch contexts; Python's signal module installs
sigaction handlers regardless of inherited disposition.
"""
import os
import signal
import sys
import time

output = ""
args = sys.argv[1:]
i = 0
while i < len(args):
    if args[i] == "-o" and i + 1 < len(args):
        output = args[i + 1]
        i += 2
    else:
        i += 1


def write_output():
    if output:
        with open(output, "w") as fh:
            fh.write("fake-recording")


def on_sigint(signum, frame):
    print("GOT_SIGINT", flush=True)
    write_output()
    sys.exit(0)


def on_sigusr1(signum, frame):
    print("GOT_SIGUSR1", flush=True)
    write_output()


signal.signal(signal.SIGINT, on_sigint)
signal.signal(signal.SIGUSR1, on_sigusr1)

print(f"STARTED PID={os.getpid()}", flush=True)
while True:
    time.sleep(0.5)
