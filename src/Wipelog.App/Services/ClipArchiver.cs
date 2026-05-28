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

using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Wipelog.Library;

namespace Wipelog.App.Services;

public sealed class ClipArchiver : IClipArchiver
{
    private readonly ILogger<ClipArchiver> _logger;

    public ClipArchiver(ILogger<ClipArchiver> logger)
    {
        _logger = logger;
    }

    public async Task<ArchiveResult> ArchiveAsync(
        IReadOnlyList<ClipRecord> clips,
        string outputPath,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        int succeeded = 0;
        int failed = 0;
        long bytesWritten = 0;
        bool cancelled = false;

        try
        {
            await using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                for (int i = 0; i < clips.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var clip = clips[i];
                    var stem = Path.GetFileNameWithoutExtension(clip.VideoPath);
                    progress?.Report(new ArchiveProgress(i, clips.Count, stem, bytesWritten));

                    try
                    {
                        // mp4 is already H.264-compressed; deflate on it gains <1%
                        // and burns time. Sidecars are tiny JSON so default fastest
                        // compression keeps them readable in the zip listing.
                        bytesWritten += await CopyToEntryAsync(
                            zip, clip.VideoPath, stem + ".mp4",
                            CompressionLevel.NoCompression, ct);
                        bytesWritten += await CopyToEntryAsync(
                            zip, clip.SidecarPath, stem + ".json",
                            CompressionLevel.Fastest, ct);

                        // Events sidecar is optional — older clips don't have it.
                        var eventsPath = Path.ChangeExtension(clip.VideoPath, null) + ".events.json";
                        if (File.Exists(eventsPath))
                        {
                            bytesWritten += await CopyToEntryAsync(
                                zip, eventsPath, stem + ".events.json",
                                CompressionLevel.Fastest, ct);
                        }
                        succeeded++;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Archive: clip {Stem} failed; continuing.", stem);
                        failed++;
                    }
                }

                // Final tick so the bar reaches 100% before the dialog closes.
                progress?.Report(new ArchiveProgress(clips.Count, clips.Count, "", bytesWritten));
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive: aborted writing {Path}", outputPath);
            cancelled = true;
        }

        if (cancelled)
        {
            // The using-blocks above flushed and closed the file; safe to delete.
            try { File.Delete(outputPath); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Archive: failed to delete partial output {Path}", outputPath);
            }
        }

        return new ArchiveResult(succeeded, failed, bytesWritten, outputPath, cancelled);
    }

    private static async Task<long> CopyToEntryAsync(
        ZipArchive zip, string sourcePath, string entryName,
        CompressionLevel level, CancellationToken ct)
    {
        var entry = zip.CreateEntry(entryName, level);
        await using var input = File.OpenRead(sourcePath);
        await using var output = entry.Open();
        await input.CopyToAsync(output, ct);
        return input.Length;
    }
}
