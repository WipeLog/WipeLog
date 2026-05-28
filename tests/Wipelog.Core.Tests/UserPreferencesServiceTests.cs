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
using Xunit;

namespace Wipelog.Core.Tests;

public class UserPreferencesServiceTests
{
    [Fact]
    public void Load_returns_defaults_when_file_missing()
    {
        using var dir = new TempDirectory();
        var svc = new UserPreferencesService(Path.Combine(dir.Path, "prefs.json"));
        var prefs = svc.Load();
        Assert.True(prefs.CloseToTray);
        Assert.True(prefs.NotifyOnClipSaved);
        Assert.True(prefs.NotifyOnErrors);
    }

    [Fact]
    public void Save_then_Load_roundtrips_all_fields()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        var writer = new UserPreferencesService(path);
        writer.Save(new UserPreferences
        {
            CloseToTray = false,
            NotifyOnClipSaved = false,
            NotifyOnErrors = true,
        });

        var reader = new UserPreferencesService(path);
        var read = reader.Load();
        Assert.False(read.CloseToTray);
        Assert.False(read.NotifyOnClipSaved);
        Assert.True(read.NotifyOnErrors);
    }

    [Fact]
    public void Load_falls_back_to_defaults_on_malformed_json()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        File.WriteAllText(path, "{ broken");
        var svc = new UserPreferencesService(path);
        var prefs = svc.Load();
        Assert.True(prefs.NotifyOnClipSaved); // default
    }

    [Fact]
    public void Save_then_Load_roundtrips_daemon_config()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        var writer = new UserPreferencesService(path);
        writer.Save(new UserPreferences
        {
            Daemon = new DaemonConfig
            {
                DaemonExecutablePath = "/opt/wipelog/daemon",
                LogDirectory = "/home/u/Logs",
                OutputDirectory = "/home/u/Videos/wow",
                Framerate = 144,
                VideoQuality = "very_high",
                Codec = "hevc",
            },
        });

        var read = new UserPreferencesService(path).Load();
        Assert.Equal("/opt/wipelog/daemon", read.Daemon.DaemonExecutablePath);
        Assert.Equal("/home/u/Logs", read.Daemon.LogDirectory);
        Assert.Equal(144, read.Daemon.Framerate);
        Assert.Equal("very_high", read.Daemon.VideoQuality);
        Assert.Equal("hevc", read.Daemon.Codec);
        // Defaults preserved.
        Assert.Equal("WoWCombatLog-*.txt", read.Daemon.LogPattern);
        Assert.Equal("mp4", read.Daemon.Container);
    }

    [Fact]
    public void Load_old_file_without_daemon_section_gives_home_defaults()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        File.WriteAllText(path,
            "{\"closeToTray\":true,\"notifyOnClipSaved\":false,\"notifyOnErrors\":true}");

        var prefs = new UserPreferencesService(path).Load();
        Assert.False(prefs.NotifyOnClipSaved);
        // Daemon section defaults are populated from $HOME so the install
        // form starts with usable values rather than empty strings.
        Assert.NotEmpty(prefs.Daemon.DaemonExecutablePath);
        Assert.NotEmpty(prefs.Daemon.OutputDirectory);
    }

    [Fact]
    public void Load_old_file_without_storage_section_gives_default_floor()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        File.WriteAllText(path, "{\"closeToTray\":true}");

        var prefs = new UserPreferencesService(path).Load();
        Assert.Equal(50, prefs.StorageManagement.MinFreeGigabytes);
    }

    [Fact]
    public void Save_then_Load_roundtrips_free_space_floor()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        new UserPreferencesService(path).Save(new UserPreferences
        {
            StorageManagement = new StorageManagement { MinFreeGigabytes = 200 },
        });

        var read = new UserPreferencesService(path).Load();
        Assert.Equal(200, read.StorageManagement.MinFreeGigabytes);
    }

    [Fact]
    public void Load_migrates_legacy_maxStorageGigabytes_to_free_floor()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        // A file written by the earlier cap-based build.
        File.WriteAllText(path, "{\"storageManagement\":{\"maxStorageGigabytes\":120}}");

        var read = new UserPreferencesService(path).Load();
        Assert.Equal(120, read.StorageManagement.MinFreeGigabytes);

        // And the legacy key is dropped on the next save.
        new UserPreferencesService(path).Save(read);
        Assert.DoesNotContain("maxStorageGigabytes", File.ReadAllText(path));
    }

    [Fact]
    public void ChatOverlay_defaults_to_off_flush_with_the_bottom_left()
    {
        var overlay = new ChatOverlay();
        Assert.False(overlay.Enabled);
        Assert.Equal(0, overlay.X);
        Assert.Equal(0, overlay.Y); // Y is from the bottom; 0 == flush with the bottom edge.
        Assert.Equal(540, overlay.Width);
        Assert.Equal(320, overlay.Height);
    }

    [Fact]
    public void Load_old_file_without_chat_overlay_section_gives_defaults()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        File.WriteAllText(path, "{\"closeToTray\":true}");

        var prefs = new UserPreferencesService(path).Load();
        Assert.False(prefs.ChatOverlay.Enabled);
        Assert.Equal(540, prefs.ChatOverlay.Width);
        Assert.Equal(320, prefs.ChatOverlay.Height);
    }

    [Fact]
    public void Save_then_Load_roundtrips_chat_overlay()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "prefs.json");
        new UserPreferencesService(path).Save(new UserPreferences
        {
            ChatOverlay = new ChatOverlay
            {
                Enabled = true, X = 12, Y = 800, Width = 600, Height = 400,
            },
        });

        var read = new UserPreferencesService(path).Load();
        Assert.True(read.ChatOverlay.Enabled);
        Assert.Equal(12, read.ChatOverlay.X);
        Assert.Equal(800, read.ChatOverlay.Y);
        Assert.Equal(600, read.ChatOverlay.Width);
        Assert.Equal(400, read.ChatOverlay.Height);
    }
}
