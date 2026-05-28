#!/usr/bin/env python3
"""Fake gpu-screen-recorder that prints to stderr and exits non-zero.

Used to test that GpuScreenRecorder detects unexpected death of the
underlying process, captures its stderr tail, and exposes the exit code.
"""
import sys
import time

print("ERROR: simulated capture failure", file=sys.stderr, flush=True)
print("ERROR: cannot acquire focused window", file=sys.stderr, flush=True)
# Give the parent a moment to attach Exited and stderr handlers before we
# die — otherwise we sometimes race the .NET process startup machinery.
time.sleep(0.05)
sys.exit(7)
