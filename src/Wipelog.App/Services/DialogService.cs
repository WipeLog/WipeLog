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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using SukiUI.Dialogs;

namespace Wipelog.App.Services;

/// <summary>
/// Dialog helpers implemented over SukiUI's in-window <see cref="ISukiDialogManager"/>
/// (bound to the SukiDialogHost in SukiWindow). The native file picker still uses
/// the platform StorageProvider since SukiUI has no file dialog.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly ISukiDialogManager _dialogs;

    public DialogService(ISukiDialogManager dialogs) => _dialogs = dialogs;

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Delete", string cancelLabel = "Cancel")
    {
        var tcs = new TaskCompletionSource<bool>();
        _dialogs.CreateDialog()
            .WithTitle(title)
            .WithContent(message)
            .WithActionButton(confirmLabel, _ => tcs.TrySetResult(true), dismissOnClick: true)
            .WithActionButton(cancelLabel, _ => tcs.TrySetResult(false), dismissOnClick: true)
            .TryShow();
        return tcs.Task;
    }

    public async Task<string?> SaveFilePickerAsync(
        string suggestedName,
        string filterName,
        IReadOnlyList<string> extensions,
        CancellationToken ct)
    {
        var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) return null;

        // Convert ".zip" → "*.zip" for the picker's glob patterns.
        var patterns = extensions
            .Select(e => e.StartsWith('*') ? e : "*" + (e.StartsWith('.') ? e : "." + e))
            .ToArray();
        var fileType = new FilePickerFileType(filterName) { Patterns = patterns };

        var result = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName,
            FileTypeChoices = new[] { fileType },
            DefaultExtension = extensions.Count > 0
                ? extensions[0].TrimStart('.')
                : null,
            ShowOverwritePrompt = true,
        });

        // IStorageFile.Path is a URI — TryGetLocalPath maps it back to a
        // filesystem path on platforms where one exists (everywhere we run).
        return result?.TryGetLocalPath();
    }

    public async Task<T> ShowProgressAsync<T>(
        string title,
        Func<IProgress<ProgressUpdate>, CancellationToken, Task<T>> work)
    {
        using var cts = new CancellationTokenSource();

        var subtitle = new TextBlock { Text = string.Empty, Opacity = 0.8 };
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, MinWidth = 320 };
        var content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 340,
            Children = { subtitle, bar },
        };

        // Cancel doesn't auto-dismiss — we dismiss once the work's finally runs,
        // so the dialog stays up through any cancellation cleanup.
        _dialogs.CreateDialog()
            .WithTitle(title)
            .WithContent(content)
            .WithActionButton("Cancel", _ => cts.Cancel(), dismissOnClick: false)
            .TryShow();

        // Progress<T> captures the UI SynchronizationContext, so these callbacks
        // marshal back to the UI thread automatically.
        var progress = new Progress<ProgressUpdate>(p =>
        {
            subtitle.Text = p.Subtitle;
            bar.Value = p.Percent;
        });

        try
        {
            return await work(progress, cts.Token);
        }
        finally
        {
            _dialogs.DismissDialog();
        }
    }
}
