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

namespace Wipelog.Daemon.Notifications;

public interface INotificationService
{
    Task NotifyClipSavedAsync(ClipFinalization clip, CancellationToken ct);
    Task NotifyErrorAsync(string title, string message, CancellationToken ct);

    /// <summary>Free disk space is nearing or below the configured floor.</summary>
    Task NotifyDiskWarningAsync(long freeGb, long minFreeGb, CancellationToken ct);

    /// <summary>Summary after the daemon auto-deleted old clips to free space.</summary>
    Task NotifyAutoDeleteAsync(int deletedCount, long bytesFreed, CancellationToken ct);

    /// <summary>Free space below the floor but every remaining clip is favorited — manual cleanup needed.</summary>
    Task NotifyDiskFullFavoritesOnlyAsync(CancellationToken ct);
}
