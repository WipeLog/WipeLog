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

using System.Globalization;

namespace Wipelog.Core.Recording;

/// <summary>
/// Combines <c>outputDirectory</c> with the suggested filename and appends
/// <c>-1</c>, <c>-2</c>, ... before the extension if a file already exists
/// at that path. Side effect: probes the filesystem with
/// <see cref="File.Exists(string)"/>.
/// </summary>
public sealed class DefaultClipNamingStrategy : IClipNamingStrategy
{
    public string Resolve(string outputDirectory, string suggestedFilename)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFilename);

        var basePath = Path.Combine(outputDirectory, suggestedFilename);
        if (!File.Exists(basePath))
        {
            return basePath;
        }

        var dir = Path.GetDirectoryName(basePath) ?? outputDirectory;
        var stem = Path.GetFileNameWithoutExtension(suggestedFilename);
        var ext = Path.GetExtension(suggestedFilename);

        for (int n = 1; n < int.MaxValue; n++)
        {
            var candidate = Path.Combine(
                dir,
                $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException(
            $"Exhausted collision suffixes for '{suggestedFilename}' in '{outputDirectory}'.");
    }
}
