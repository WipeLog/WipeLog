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

public class AudioTrackNamingTests
{
    [Theory]
    [InlineData("default_output", "System mix")]
    [InlineData("default_input", "Microphone")]
    [InlineData("app:World of Warcraft", "World of Warcraft")]
    [InlineData("app:spotify", "spotify")]
    [InlineData("app:WEBRTC VoiceEngine", "WEBRTC VoiceEngine")]
    [InlineData("app-inverse:spotify", "Not spotify")]
    [InlineData("device:alsa_output.pci-0000_00_1f.3", "alsa_output.pci-0000_00_1f.3")]
    [InlineData("custom-pulseaudio-sink", "custom-pulseaudio-sink")]
    public void Maps_known_source_forms_to_friendly_titles(string source, string expected)
    {
        Assert.Equal(expected, AudioTrackNaming.FriendlyTitle(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_yields_fallback_label(string source)
    {
        Assert.Equal("Audio", AudioTrackNaming.FriendlyTitle(source));
    }

    [Fact]
    public void Strips_prefix_case_insensitively_and_trims_remainder()
    {
        // gpu-screen-recorder accepts the prefix in lowercase but tolerate
        // a user typing `App:` in settings — the friendly name shouldn't
        // include the prefix or any leading whitespace.
        Assert.Equal("Spotify", AudioTrackNaming.FriendlyTitle("App: Spotify"));
    }

    [Fact]
    public void Bare_prefix_with_no_name_falls_through_to_passthrough()
    {
        // `app:` with nothing after it isn't a meaningful split — keep the
        // original so the user sees the misconfiguration in VLC.
        Assert.Equal("app:", AudioTrackNaming.FriendlyTitle("app:"));
    }

    [Fact]
    public void FriendlyTitles_preserves_order_for_the_split_by_app_preset()
    {
        var sources = new[]
        {
            "app:World of Warcraft",
            "app:spotify",
            "app:WEBRTC VoiceEngine",
            "default_output",
        };
        var titles = AudioTrackNaming.FriendlyTitles(sources);
        Assert.Equal(new[] { "World of Warcraft", "spotify", "WEBRTC VoiceEngine", "System mix" }, titles);
    }

    [Fact]
    public void FriendlyTitles_returns_empty_for_null_or_empty_input()
    {
        Assert.Empty(AudioTrackNaming.FriendlyTitles(null));
        Assert.Empty(AudioTrackNaming.FriendlyTitles(Array.Empty<string>()));
    }
}
