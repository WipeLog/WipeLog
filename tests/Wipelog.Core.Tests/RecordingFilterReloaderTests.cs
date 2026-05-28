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

using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.Detection;
using Wipelog.Core.Preferences;
using Wipelog.Daemon.Configuration;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecordingFilterReloaderTests
{
    [Fact]
    public void ReloadNow_pushes_saved_filters_into_the_detector()
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, "preferences.json");
        var prefs = new UserPreferencesService(path);
        prefs.Save(new UserPreferences
        {
            RecordingFilters = new RecordingFilters { MinKeystoneLevel = 14, RecordBattlegrounds = false },
        });

        var detector = new EncounterDetector(filters: new RecordingFilters { MinKeystoneLevel = 2 });
        var reloader = new RecordingFilterReloader(
            detector, NullLogger<RecordingFilterReloader>.Instance, prefs);

        reloader.ReloadNow();

        Assert.Equal(14, detector.CurrentFilters.MinKeystoneLevel);
        Assert.False(detector.CurrentFilters.RecordBattlegrounds);
    }

    [Fact]
    public void ReloadNow_restores_defaults_when_preferences_file_is_missing()
    {
        using var dir = new TempDirectory();
        var path = System.IO.Path.Combine(dir.Path, "does-not-exist.json");
        var prefs = new UserPreferencesService(path);

        var detector = new EncounterDetector(filters: new RecordingFilters { MinKeystoneLevel = 14 });
        var reloader = new RecordingFilterReloader(
            detector, NullLogger<RecordingFilterReloader>.Instance, prefs);

        reloader.ReloadNow();

        // Missing file → default filters (record everything, +2 minimum).
        Assert.Equal(2, detector.CurrentFilters.MinKeystoneLevel);
    }
}
