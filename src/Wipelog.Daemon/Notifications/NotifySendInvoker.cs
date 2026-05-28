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

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Wipelog.Daemon.Notifications;

internal sealed class NotifySendInvoker : INotifySendInvoker
{
    private readonly ILogger<NotifySendInvoker> _logger;
    private bool _missingWarned;

    public NotifySendInvoker(ILogger<NotifySendInvoker> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("notify-send")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                WarnMissing();
                return;
            }
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            WarnMissing();
        }
        catch (FileNotFoundException)
        {
            WarnMissing();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "notify-send invocation failed.");
        }
    }

    private void WarnMissing()
    {
        if (_missingWarned) return;
        _missingWarned = true;
        _logger.LogWarning(
            "notify-send not found on PATH — desktop notifications disabled. " +
            "Install libnotify (e.g. `pacman -S libnotify` or `apt install libnotify-bin`).");
    }
}
