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

namespace Wipelog.App.Services;

public sealed class SystemctlDaemonLifecycleController : IDaemonLifecycleController
{
    private const string ServiceName = "wipelog.service";

    private readonly ILogger<SystemctlDaemonLifecycleController>? _logger;

    public SystemctlDaemonLifecycleController(ILogger<SystemctlDaemonLifecycleController>? logger = null)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct) => RunAsync("start", ct);
    public Task StopAsync(CancellationToken ct) => RunAsync("stop", ct);
    public Task RestartAsync(CancellationToken ct) => RunAsync("restart", ct);

    public Task OpenJournalAsync(CancellationToken ct)
    {
        try
        {
            // Prefer the freedesktop xdg-terminal-exec spec; fall back to common terminals.
            var args = new[] { "--user", "-u", ServiceName, "-f", "-n", "200" };
            var terminals = new (string Exe, string[] LeadArgs)[]
            {
                ("xdg-terminal-exec", Array.Empty<string>()),
                ("x-terminal-emulator", new[] { "-e" }),
                ("kitty", new[] { "--" }),
                ("alacritty", new[] { "-e" }),
                ("foot", new[] { "--" }),
                ("xterm", new[] { "-e" }),
            };
            foreach (var (exe, lead) in terminals)
            {
                try
                {
                    var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
                    foreach (var a in lead) psi.ArgumentList.Add(a);
                    psi.ArgumentList.Add("journalctl");
                    foreach (var a in args) psi.ArgumentList.Add(a);
                    Process.Start(psi);
                    return Task.CompletedTask;
                }
                catch (System.ComponentModel.Win32Exception) { /* try next */ }
            }
            _logger?.LogWarning("No supported terminal emulator found for opening the journal.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open systemd journal.");
        }
        return Task.CompletedTask;
    }

    private async Task RunAsync(string verb, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("systemctl")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--user");
        psi.ArgumentList.Add(verb);
        psi.ArgumentList.Add(ServiceName);
        try
        {
            using var p = Process.Start(psi);
            if (p is null) return;
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "systemctl --user {Verb} {Service} failed.", verb, ServiceName);
        }
    }
}
