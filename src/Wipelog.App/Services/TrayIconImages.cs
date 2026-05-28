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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Wipelog.App.Services;

/// <summary>
/// Loads the three tray icon variants from embedded PNGs in Assets/Icons/.
/// The PNGs are rasterized from the corresponding tray-*.svg sources via
/// rsvg-convert and checked in; regenerate them whenever the SVG changes.
/// </summary>
internal static class TrayIconImages
{
    public static WindowIcon Idle { get; } = Load("tray-idle.png");
    public static WindowIcon Recording { get; } = Load("tray-recording.png");
    public static WindowIcon Offline { get; } = Load("tray-offline.png");

    private static WindowIcon Load(string fileName)
    {
        var uri = new Uri($"avares://Wipelog.App/Assets/Icons/{fileName}");
        using var stream = AssetLoader.Open(uri);
        return new WindowIcon(stream);
    }
}
