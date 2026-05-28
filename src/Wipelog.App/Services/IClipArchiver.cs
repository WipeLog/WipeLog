// Wipelog — Linux-native World of Warcraft gameplay recorder.
// Copyright (C) 2026 Patrik - WipeLog-dev
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using Wipelog.Library;

namespace Wipelog.App.Services;

/// <summary>
/// Packages selected clips into a single .zip on disk.
/// </summary>
public interface IClipArchiver
{
    /// <summary>
    /// Streams each clip's video plus its two sidecars into <paramref name="outputPath"/>.
    /// Reports progress per clip (not per byte). On cancellation, disposes the
    /// archive cleanly and deletes the partial output so the user isn't left
    /// with a half-written file.
    /// </summary>
    Task<ArchiveResult> ArchiveAsync(
        IReadOnlyList<ClipRecord> clips,
        string outputPath,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct);
}

/// <summary>One per-clip progress tick during a bulk archive.</summary>
public sealed record ArchiveProgress(
    int CompletedClips,
    int TotalClips,
    string CurrentName,
    long BytesWritten);

/// <summary>Outcome summary returned at the end of an archive run.</summary>
public sealed record ArchiveResult(
    int Succeeded,
    int Failed,
    long TotalBytes,
    string OutputPath,
    bool WasCancelled);
