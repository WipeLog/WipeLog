#!/usr/bin/env bash
set -euo pipefail
# IncludeNativeLibrariesForSelfExtract bundles SkiaSharp/HarfBuzz/SQLite/etc.
# into the single-file blob and extracts them at startup. Without it the
# launcher dies on `libSkiaSharp.so: cannot open shared object file`.
dotnet publish src/Wipelog.Daemon -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o dist/daemon
dotnet publish src/Wipelog.App    -c Release -r linux-x64 --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o dist/app
echo "Built:"
echo "  dist/daemon/Wipelog.Daemon"
echo "  dist/app/Wipelog.App"
