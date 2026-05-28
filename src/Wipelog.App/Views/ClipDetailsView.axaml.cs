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
using Microsoft.Extensions.DependencyInjection;
using Wipelog.App.ViewModels;

namespace Wipelog.App.Views;

public partial class ClipDetailsView : UserControl
{
    public ClipDetailsView() { InitializeComponent(); }

    /// <summary>
    /// Persists pending Notes edits when the textarea loses focus. Fire-and-
    /// forget because the user expects no UI feedback for a successful save;
    /// the VM logs failures.
    /// </summary>
    private void OnNotesBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ClipDetailsViewModel vm)
        {
            _ = vm.CommitNotesAsync();
        }
    }

    /// <summary>
    /// Jumps the player to the timestamp on a clicked chip. The chip's Tag
    /// carries the bound NoteTimestamp; PlayerViewModel comes through DI
    /// (same pattern as ClipsTableView's context-menu handlers).
    /// </summary>
    private void OnTimestampChipClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not NoteTimestamp ts) return;
        if (App.Current is not App app) return;
        var player = app.Services.GetRequiredService<PlayerViewModel>();
        player.SeekAbsolute(ts.Offset);
    }
}
