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

namespace Wipelog.App.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message, string confirmLabel = "Delete", string cancelLabel = "Cancel");

    /// <summary>
    /// Prompts the user for a save destination via the native file picker.
    /// </summary>
    /// <param name="suggestedName">Pre-filled filename, e.g. <c>wipelog-clips-2026-05-12.zip</c>.</param>
    /// <param name="filterName">Display name of the file-type filter, e.g. <c>ZIP archive</c>.</param>
    /// <param name="extensions">Extensions including the leading dot, e.g. <c>[".zip"]</c>.</param>
    /// <returns>Absolute filesystem path to the chosen destination, or <c>null</c> if the user cancelled.</returns>
    Task<string?> SaveFilePickerAsync(
        string suggestedName,
        string filterName,
        IReadOnlyList<string> extensions,
        CancellationToken ct);

    /// <summary>
    /// Shows a modal progress dialog while <paramref name="work"/> runs.
    /// The work callback receives an <see cref="IProgress{ProgressUpdate}"/>
    /// to push subtitle + percent into the dialog and a cancellation token
    /// fed by the dialog's Cancel button. The dialog is dismissed when work
    /// completes (success, cancellation, or exception).
    /// </summary>
    Task<T> ShowProgressAsync<T>(
        string title,
        Func<IProgress<ProgressUpdate>, CancellationToken, Task<T>> work);
}

/// <summary>One tick of progress shown to the user during a long operation.</summary>
public sealed record ProgressUpdate(string Subtitle, double Percent);
