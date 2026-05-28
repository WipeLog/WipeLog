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

using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class XrandrMonitorListTests
{
    // Real `xrandr --listmonitors` output from a dual-head setup. The connector
    // name (last token) matches gpu-screen-recorder's --list-monitors names.
    private const string DualHead =
        "Monitors: 2\n" +
        " 0: +*DisplayPort-0 2560/598x1440/336+0+0  DisplayPort-0\n" +
        " 1: +DisplayPort-1 3840/597x2160/336+2560+0  DisplayPort-1\n";

    [Fact]
    public void Parse_extracts_name_size_and_offset()
    {
        var monitors = XrandrMonitorList.Parse(DualHead);

        Assert.Equal(2, monitors.Count);

        Assert.Equal("DisplayPort-0", monitors[0].Name);
        Assert.Equal(2560, monitors[0].Width);
        Assert.Equal(1440, monitors[0].Height);
        Assert.Equal(0, monitors[0].X);
        Assert.Equal(0, monitors[0].Y);

        // The second monitor is offset to the right of the first.
        Assert.Equal("DisplayPort-1", monitors[1].Name);
        Assert.Equal(3840, monitors[1].Width);
        Assert.Equal(2160, monitors[1].Height);
        Assert.Equal(2560, monitors[1].X);
        Assert.Equal(0, monitors[1].Y);
    }

    [Fact]
    public void Parse_ignores_the_header_and_blank_lines()
    {
        var monitors = XrandrMonitorList.Parse("Monitors: 1\n\n 0: +HDMI-1 1920/520x1080/290+0+120  HDMI-1\n");
        var m = Assert.Single(monitors);
        Assert.Equal("HDMI-1", m.Name);
        Assert.Equal(1920, m.Width);
        Assert.Equal(1080, m.Height);
        Assert.Equal(0, m.X);
        Assert.Equal(120, m.Y);
    }

    [Fact]
    public void Parse_handles_negative_offsets()
    {
        var monitors = XrandrMonitorList.Parse(" 0: +DP-2 1920/520x1080/290-1920+0  DP-2\n");
        var m = Assert.Single(monitors);
        Assert.Equal(-1920, m.X);
        Assert.Equal(0, m.Y);
    }

    [Fact]
    public void Parse_returns_empty_for_garbage()
    {
        Assert.Empty(XrandrMonitorList.Parse(""));
        Assert.Empty(XrandrMonitorList.Parse("command not found"));
    }

    [Fact]
    public void PickLargest_returns_the_biggest_area()
    {
        var monitors = XrandrMonitorList.Parse(DualHead);
        var largest = XrandrMonitorList.PickLargest(monitors);
        Assert.NotNull(largest);
        Assert.Equal("DisplayPort-1", largest!.Name); // 4K beats 1440p
    }

    [Fact]
    public void PickLargest_returns_null_for_empty()
    {
        Assert.Null(XrandrMonitorList.PickLargest(Array.Empty<XrandrMonitorList.Monitor>()));
    }
}
