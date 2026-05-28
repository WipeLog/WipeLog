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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wipelog.App.Services;

public sealed record SavedWindowState(double Width, double Height, int? X, int? Y, bool Maximized);

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SavedWindowState))]
internal partial class WindowStateJsonContext : JsonSerializerContext
{
}

public sealed class WindowStateService
{
    private readonly string _path;

    public WindowStateService()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        var baseDir = string.IsNullOrEmpty(xdg) ? System.IO.Path.Combine(home, ".config") : xdg;
        _path = System.IO.Path.Combine(baseDir, "wipelog", "window.json");
    }

    public string Path => _path;

    public SavedWindowState? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            using var stream = File.OpenRead(_path);
            return JsonSerializer.Deserialize(stream, WindowStateJsonContext.Default.SavedWindowState);
        }
        catch
        {
            return null;
        }
    }

    public void Save(SavedWindowState state)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using var stream = File.Create(_path);
            JsonSerializer.Serialize(stream, state, WindowStateJsonContext.Default.SavedWindowState);
        }
        catch
        {
            // Best-effort persistence — never crash on shutdown.
        }
    }
}
