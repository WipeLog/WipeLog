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

namespace Wipelog.App.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() { InitializeComponent(); }

    public ConfirmDialog(string title, string message, string confirmLabel, string cancelLabel)
    {
        InitializeComponent();
        Title = title;
        var titleBlock = this.FindControl<TextBlock>("TitleText");
        if (titleBlock is not null) titleBlock.Text = title;
        var msg = this.FindControl<TextBlock>("MessageText");
        if (msg is not null) msg.Text = message;
        var confirm = this.FindControl<Button>("ConfirmButton");
        if (confirm is not null) confirm.Content = confirmLabel;
        var cancel = this.FindControl<Button>("CancelButton");
        if (cancel is not null) cancel.Content = cancelLabel;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e) => Close(true);
    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);
}
