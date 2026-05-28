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
using Avalonia.VisualTree;
using Wipelog.App.ViewModels;
 
namespace Wipelog.App.Views;
 
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
 
    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel)
        {
            // The hosting MainWindowViewModel listens via routed event walk.
            // We post a logical event by toggling IsShowingSettings on the
            // top-level MainWindowViewModel found via the visual tree.
            var top = this.FindAncestorOfType<Window>();
            if (top?.DataContext is MainWindowViewModel mw)
            {
                mw.IsShowingSettings = false;
            }
        }
    }

    // The MenuFlyout for "Add running app…" lives in a popup whose visual
    // tree is separate from this view. `$parent[Window]` or compiled-binding
    // walks across the popup boundary are flaky in Avalonia 11, so we route
    // the click through code-behind and call the VM command directly.
    private void OnAudioSuggestionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi
            && mi.CommandParameter is string source
            && DataContext is SettingsViewModel vm
            && vm.AddAudioTrackCommand.CanExecute(source))
        {
            vm.AddAudioTrackCommand.Execute(source);
        }
    }
}
