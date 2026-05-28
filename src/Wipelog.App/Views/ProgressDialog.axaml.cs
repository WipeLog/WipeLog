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

public partial class ProgressDialog : Window
{
    /// <summary>
    /// Raised when the user clicks Cancel or attempts to close the window.
    /// Subscribers should signal cancellation to the running operation; the
    /// dialog stays open until the operation completes and the caller calls
    /// <see cref="DismissProgrammatically"/>.
    /// </summary>
    public event EventHandler? CancelRequested;

    private bool _allowClose;

    public ProgressDialog() { InitializeComponent(); }

    public ProgressDialog(string title) : this()
    {
        Title = title;
        TitleText.Text = title;
        SubtitleText.Text = "Starting…";
        Closing += OnClosing;
    }

    public void UpdateProgress(string subtitle, double percent)
    {
        SubtitleText.Text = subtitle;
        Bar.Value = Math.Clamp(percent, 0, 100);
    }

    /// <summary>
    /// Closes the dialog from the controller side, bypassing the cancel-
    /// translation in <see cref="OnClosing"/>.
    /// </summary>
    public void DismissProgrammatically()
    {
        _allowClose = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose) return;
        // User pressed Esc / clicked X / Alt-F4 — translate into cancellation
        // and keep the window open until the work actually completes. Without
        // this the work would orphan and the user would be back at the main
        // window thinking the operation succeeded.
        e.Cancel = true;
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
