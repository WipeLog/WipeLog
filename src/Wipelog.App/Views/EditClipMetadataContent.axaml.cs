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
using Avalonia.Markup.Xaml;
using Wipelog.App.ViewModels;

namespace Wipelog.App.Views;

/// <summary>Edit-metadata form body, hosted inside a SukiUI dialog.</summary>
public partial class EditClipMetadataContent : UserControl
{
    public EditClipMetadataContent() => AvaloniaXamlLoader.Load(this);

    private EditClipMetadataDialogViewModel? Vm => DataContext as EditClipMetadataDialogViewModel;

    // The bound RadioButton doesn't fire its IsChecked setter when toggled on;
    // mirror the value into Success here (same quirk as the old window).
    private void OnFailureClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm) vm.Success = false;
    }
}
