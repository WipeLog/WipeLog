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

using Avalonia.Controls;
using Avalonia.Interactivity;
using Wipelog.App.Controls;
using Wipelog.App.ViewModels;

namespace Wipelog.App.Views;

public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();

        var scrub = this.FindControl<ScrubBar>("ScrubBar");
        if (scrub is not null)
        {
            scrub.SeekRequested += OnScrubSeekRequested;
        }

        var tombstones = this.FindControl<TombstoneStrip>("TombstoneStrip");
        if (tombstones is not null)
        {
            tombstones.MarkerClicked += OnTombstoneMarkerClicked;
        }
    }

    private PlayerViewModel? Vm => DataContext as PlayerViewModel;

    private void OnScrubSeekRequested(object? sender, TombstoneSeekRequestedEventArgs e) =>
        Vm?.SeekAbsolute(e.Target);

    private void OnPlayPauseClick(object? sender, RoutedEventArgs e) => Vm?.TogglePlayPause();

    private void OnMuteClick(object? sender, RoutedEventArgs e) => Vm?.ToggleMute();

    private void OnTombstoneMarkerClicked(object? sender, TombstoneMarkerClickedEventArgs e)
    {
        if (Vm is null) return;
        // Boss-label clicks already arrive with SeekTarget in video-time
        // (the strip applies PreRollMs). Tombstone clicks rewind by the user's
        // preferred seek-back instead of the strip's default 5 s, so we
        // recompute here — but the event's OffsetMs is still encounter-relative,
        // so add PreRollMs back in before subtracting the rewind.
        if (e.Event is Wipelog.Core.Recording.BossStartEvent)
        {
            Vm.SeekAbsolute(e.SeekTarget);
            return;
        }
        var prefs = Vm.TimelinePreferences;
        var preRollMs = System.Math.Max(0, Vm.Timeline.PreRollMs);
        var videoMs = e.Event.OffsetMs + preRollMs;
        var seekTarget = System.TimeSpan.FromMilliseconds(
            System.Math.Max(0, videoMs - prefs.SeekBackSeconds * 1000));
        Vm.SeekAbsolute(seekTarget);
        if (prefs.PauseOnClick) Vm.Pause();
    }
}
