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

using Wipelog.App.Views;
using Xunit;

namespace Wipelog.App.Snapshots;

/// <summary>
/// Modal dialogs are snapshotted at their own declared size (420 px wide,
/// SizeToContent=Height) rather than 1280×800 — that's how users see them.
/// </summary>
public sealed class DialogSnapshots(HeadlessSessionFixture session) : IClassFixture<HeadlessSessionFixture>
{
    [Fact]
    public Task ConfirmDialog_delete() => session.Run(() =>
    {
        var dialog = new ConfirmDialog(
            "Delete 3 clips?",
            "The video files and their sidecars will be removed from disk. This cannot be undone.",
            "Delete", "Cancel");
        dialog.Show();
        Snap.All(dialog, "confirm-dialog");
        dialog.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task ProgressDialog_archiving() => session.Run(() =>
    {
        var dialog = new ProgressDialog("Archiving 12 clips");
        dialog.UpdateProgress("Ara-Kara, City of Echoes +12 (5 of 12)", 41);
        dialog.Show();
        Snap.All(dialog, "progress-dialog");
        dialog.DismissProgrammatically();
        return Task.CompletedTask;
    });
}
