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
using Wipelog.App.ViewModels;
using Wipelog.Core.Status;
using Xunit;

namespace Wipelog.Core.Tests;

public class DiskUsageDisplayTests
{
    private const long Gb = 1024L * 1024 * 1024;
    private static readonly DateTimeOffset Now = new(2026, 5, 11, 12, 0, 0, TimeSpan.Zero);

    private static DaemonStatus StatusWithDisk(DiskUsageInfo? disk) =>
        new("1", 1, Now.AddMinutes(-1), Now, DaemonState.Idle,
            CurrentRecording: null, ClipsRecordedThisSession: 0, LastError: null)
        {
            DiskUsage = disk,
        };

    [Fact]
    public void Project_offline_reports_unknown_usage()
    {
        var p = DaemonStatusViewModel.Project(null, Now);

        Assert.False(p.HasDiskUsage);
        Assert.Equal(0, p.DiskUsagePercent);
        Assert.Contains("offline", p.DiskUsageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Project_unknown_drive_shows_placeholder()
    {
        // TotalBytes == 0 → unknown (or a pre-free-space-floor payload).
        var p = DaemonStatusViewModel.Project(
            StatusWithDisk(new DiskUsageInfo(0, 0, 0, 50 * Gb, 0)), Now);

        Assert.False(p.HasDiskUsage);
        Assert.Equal("—", p.DiskUsageText);
    }

    [Fact]
    public void Project_formats_whole_disk_readout()
    {
        var d = new DiskUsageInfo(
            UsedBytes: 420 * Gb, TotalBytes: 500 * Gb, FreeBytes: 80 * Gb,
            MinFreeBytes: 50 * Gb, UsagePercent: 84.0);
        var p = DaemonStatusViewModel.Project(StatusWithDisk(d), Now);

        Assert.True(p.HasDiskUsage);
        Assert.Equal(84.0, p.DiskUsagePercent);
        Assert.Contains("420.0 GB", p.DiskUsageText);
        Assert.Contains("500.0 GB", p.DiskUsageText);
        Assert.Contains("80.0 GB free", p.DiskUsageText);
    }

    [Theory]
    [InlineData(80, 50, "AccentPrimaryBrush")] // comfortably above floor
    [InlineData(60, 50, "WarningBrush")]       // within 25% of the floor
    [InlineData(50, 50, "DangerBrush")]        // at the floor
    [InlineData(20, 50, "DangerBrush")]        // below the floor
    public void DiskBarBrushKey_tracks_free_vs_floor(long freeGb, long floorGb, string expected)
        => Assert.Equal(expected, DaemonStatusViewModel.DiskBarBrushKey(freeGb * Gb, floorGb * Gb));

    [Fact]
    public void DaemonStatus_round_trips_disk_usage()
    {
        var status = StatusWithDisk(new DiskUsageInfo(123, 456, 333, 789, 27.0));
        var json = JsonSerializer.Serialize(status);
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        var read = JsonSerializer.Deserialize<DaemonStatus>(json, opts);

        Assert.NotNull(read!.DiskUsage);
        Assert.Equal(123, read.DiskUsage!.UsedBytes);
        Assert.Equal(456, read.DiskUsage.TotalBytes);
        Assert.Equal(333, read.DiskUsage.FreeBytes);
        Assert.Equal(789, read.DiskUsage.MinFreeBytes);
        Assert.Equal(27.0, read.DiskUsage.UsagePercent);
    }

    [Fact]
    public void DaemonStatus_old_payload_deserializes_with_null_disk_usage()
    {
        const string old = """
            {"version":"1","pid":1,"startedAt":"2026-05-11T11:59:00+00:00",
             "heartbeatAt":"2026-05-11T12:00:00+00:00","state":"Idle",
             "currentRecording":null,"clipsRecordedThisSession":0,"lastError":null}
            """;
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        var read = JsonSerializer.Deserialize<DaemonStatus>(old, opts);

        Assert.NotNull(read);
        Assert.Null(read!.DiskUsage);
    }

    [Fact]
    public void MaxFreeGigabytes_unknown_drive_falls_back_to_2000()
        => Assert.Equal(2000, SettingsViewModel.MaxFreeGigabytesForDisk(null, floorGb: 5));

    [Fact]
    public void MaxFreeGigabytes_clamps_to_drive_total()
        => Assert.Equal(500, SettingsViewModel.MaxFreeGigabytesForDisk(
            new Wipelog.Core.Storage.DiskSpaceInfo(100 * Gb, 500 * Gb), floorGb: 5));

    [Fact]
    public void MaxFreeGigabytes_never_below_floor()
        => Assert.Equal(5, SettingsViewModel.MaxFreeGigabytesForDisk(
            new Wipelog.Core.Storage.DiskSpaceInfo(1 * Gb, 2 * Gb), floorGb: 5));
}
