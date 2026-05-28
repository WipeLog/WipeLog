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

namespace Wipelog.Core.Storage;

/// <summary>Free and total bytes of the filesystem a path lives on.</summary>
public sealed record DiskSpaceInfo(long FreeBytes, long TotalBytes);

/// <summary>
/// Reports the free/total space of the drive holding a given path. Abstracted
/// so the daemon's effective-cap math and the GUI's cap clamp share one
/// implementation, and tests can supply synthetic disk sizes.
/// </summary>
public interface IDiskSpaceProbe
{
    /// <summary>Drive space for <paramref name="path"/>, or null when it can't be determined.</summary>
    DiskSpaceInfo? Probe(string path);
}

/// <summary>
/// Real-filesystem probe. Resolves the nearest existing ancestor of the path
/// (the output dir may not exist yet on first run) and matches it to the mounted
/// drive with the longest root-prefix, so nested mounts resolve correctly.
/// </summary>
public sealed class SystemDiskSpaceProbe : IDiskSpaceProbe
{
    public static readonly SystemDiskSpaceProbe Instance = new();

    public DiskSpaceInfo? Probe(string path)
    {
        try
        {
            var existing = NearestExistingAncestor(path);
            if (existing is null) return null;
            var full = Path.GetFullPath(existing);

            DriveInfo? best = null;
            int bestLen = -1;
            foreach (var drive in DriveInfo.GetDrives())
            {
                bool ready;
                try { ready = drive.IsReady; }
                catch { continue; }
                if (!ready) continue;

                var root = drive.RootDirectory.FullName;
                if (IsPrefixOf(root, full) && root.Length > bestLen)
                {
                    best = drive;
                    bestLen = root.Length;
                }
            }

            if (best is null) return null;
            return new DiskSpaceInfo(best.AvailableFreeSpace, best.TotalSize);
        }
        catch
        {
            return null;
        }
    }

    private static string? NearestExistingAncestor(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var dir = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(dir))
        {
            if (Directory.Exists(dir)) return dir;
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir) break;
            dir = parent;
        }
        return Directory.Exists("/") ? "/" : null;
    }

    private static bool IsPrefixOf(string root, string full)
    {
        if (full.Equals(root, StringComparison.Ordinal)) return true;
        // Ensure "/" matches everything and "/mnt/data" doesn't match "/mnt/datax".
        var withSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(withSep, StringComparison.Ordinal);
    }
}
