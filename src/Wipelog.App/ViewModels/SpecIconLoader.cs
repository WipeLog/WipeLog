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

using System.Collections.Concurrent;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Wipelog.Core.Data;

namespace Wipelog.App.ViewModels;

/// <summary>
/// Resolves a (class, spec) pair to a cached <see cref="Bitmap"/> backed by
/// the AvaloniaResource jpgs under Assets/Icons/Specs. Returns null when the
/// pair is unknown so callers can fall back to the class-color swatch. The
/// 39-entry cache is bounded by <see cref="SpecIcons.Known"/>.
/// </summary>
internal static class SpecIconLoader
{
    private static readonly ConcurrentDictionary<string, IImage> Cache = new();

    public static IImage? Load(string? playerClass, string? playerSpec)
    {
        var basename = SpecIcons.Lookup(playerClass, playerSpec);
        if (basename is null) return null;
        return Cache.GetOrAdd(basename, LoadBitmap);
    }

    private static IImage LoadBitmap(string basename)
    {
        var uri = new Uri($"avares://Wipelog.App/Assets/Icons/Specs/{basename}.jpg");
        using var stream = AssetLoader.Open(uri);
        return new Bitmap(stream);
    }
}
