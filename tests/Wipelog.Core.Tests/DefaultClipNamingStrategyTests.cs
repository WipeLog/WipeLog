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

public class DefaultClipNamingStrategyTests
{
    [Fact]
    public void Returns_combined_path_when_no_collision()
    {
        using var dir = new TempDirectory();
        var strategy = new DefaultClipNamingStrategy();

        var resolved = strategy.Resolve(dir.Path, "clip.mp4");

        Assert.Equal(Path.Combine(dir.Path, "clip.mp4"), resolved);
    }

    [Fact]
    public void Appends_dash_one_when_base_exists()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "clip.mp4"), "x");
        var strategy = new DefaultClipNamingStrategy();

        var resolved = strategy.Resolve(dir.Path, "clip.mp4");

        Assert.Equal(Path.Combine(dir.Path, "clip-1.mp4"), resolved);
    }

    [Fact]
    public void Increments_collision_suffix_until_free_slot_found()
    {
        using var dir = new TempDirectory();
        File.WriteAllText(Path.Combine(dir.Path, "clip.mp4"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "clip-1.mp4"), "x");
        File.WriteAllText(Path.Combine(dir.Path, "clip-2.mp4"), "x");
        var strategy = new DefaultClipNamingStrategy();

        var resolved = strategy.Resolve(dir.Path, "clip.mp4");

        Assert.Equal(Path.Combine(dir.Path, "clip-3.mp4"), resolved);
    }
}
