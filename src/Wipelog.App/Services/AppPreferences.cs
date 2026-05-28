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

using Wipelog.Core.Preferences;

namespace Wipelog.App.Services;

// Re-export under the App-facing names for backwards compatibility within the
// project. The actual contract lives in Wipelog.Core.Preferences so the
// daemon can read the same file.
public sealed record AppPreferences
{
    public bool CloseToTray { get; init; } = true;
    public bool NotifyOnClipSaved { get; init; } = true;
    public bool NotifyOnErrors { get; init; } = true;

    internal UserPreferences ToCore() => new()
    {
        CloseToTray = CloseToTray,
        NotifyOnClipSaved = NotifyOnClipSaved,
        NotifyOnErrors = NotifyOnErrors,
    };

    internal static AppPreferences FromCore(UserPreferences prefs) => new()
    {
        CloseToTray = prefs.CloseToTray,
        NotifyOnClipSaved = prefs.NotifyOnClipSaved,
        NotifyOnErrors = prefs.NotifyOnErrors,
    };
}

public sealed class AppPreferencesService
{
    private readonly UserPreferencesService _inner;

    public AppPreferencesService() : this(new UserPreferencesService()) { }

    public AppPreferencesService(UserPreferencesService inner)
    {
        _inner = inner;
    }

    public string Path => _inner.Path;

    public static string DefaultPath() => UserPreferencesService.DefaultPath();

    public AppPreferences Load() => AppPreferences.FromCore(_inner.Load());

    public void Save(AppPreferences prefs) => _inner.Save(prefs.ToCore());
}
