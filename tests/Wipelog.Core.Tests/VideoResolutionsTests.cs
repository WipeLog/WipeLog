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

public class VideoResolutionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("source")]
    [InlineData("1080p")]
    [InlineData("1440P")]   // case-insensitive
    public void IsValid_accepts_recognised_choices(string? value)
    {
        Assert.True(VideoResolutions.IsValid(value));
    }

    [Theory]
    [InlineData("4k")]
    [InlineData("2160p")]
    [InlineData("garbage")]
    public void IsValid_rejects_unknown_choices(string value)
    {
        Assert.False(VideoResolutions.IsValid(value));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("source", null)]
    [InlineData("720p", 720)]
    [InlineData("1080p", 1080)]
    [InlineData("1440p", 1440)]
    public void TargetHeight_maps_to_height_or_null_for_source(string? input, int? expected)
    {
        Assert.Equal(expected, VideoResolutions.TargetHeight(input));
    }

    [Fact]
    public void ResolveCaptureSize_returns_null_when_no_target()
    {
        Assert.Null(VideoResolutions.ResolveCaptureSize(3840, 2160, null));
    }

    [Fact]
    public void ResolveCaptureSize_returns_null_when_target_is_not_smaller()
    {
        // 1440p on a 1440p monitor: no point scaling.
        Assert.Null(VideoResolutions.ResolveCaptureSize(2560, 1440, 1440));
        // 1440p on a 1080p monitor: don't upscale either.
        Assert.Null(VideoResolutions.ResolveCaptureSize(1920, 1080, 1440));
    }

    [Fact]
    public void ResolveCaptureSize_preserves_aspect_ratio_for_16_9_4k()
    {
        // 3840x2160 → 1080p preserves AR exactly: 1920x1080.
        Assert.Equal("1920x1080", VideoResolutions.ResolveCaptureSize(3840, 2160, 1080));
    }

    [Fact]
    public void ResolveCaptureSize_preserves_aspect_ratio_for_ultrawide()
    {
        // 3440x1440 ultrawide → 1080p: width = round(3440 * 1080 / 1440) = 2580.
        Assert.Equal("2580x1080", VideoResolutions.ResolveCaptureSize(3440, 1440, 1080));
    }

    [Fact]
    public void ResolveCaptureSize_rounds_width_to_even()
    {
        // Odd-width AR: 1366x768 → 720p: round(1366 * 720 / 768) = 1281 → 1282 (even).
        var size = VideoResolutions.ResolveCaptureSize(1366, 768, 720);
        Assert.NotNull(size);
        var w = int.Parse(size!.Split('x')[0]);
        Assert.Equal(0, w % 2);
    }
}
