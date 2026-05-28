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
using System.Text;
using Microsoft.Extensions.Logging;
using Wipelog.Core.Preferences;

namespace Wipelog.App.Services;

public sealed class SystemdServiceInstaller : IServiceInstaller
{
    private const string ServiceName = "wipelog.service";

    private readonly ILogger<SystemdServiceInstaller>? _logger;
    private readonly string _serviceFilePath;

    public SystemdServiceInstaller(ILogger<SystemdServiceInstaller>? logger = null)
        : this(DefaultServiceFilePath(), logger)
    {
    }

    public SystemdServiceInstaller(string serviceFilePath, ILogger<SystemdServiceInstaller>? logger = null)
    {
        _serviceFilePath = serviceFilePath;
        _logger = logger;
    }

    public string ServiceFilePath => _serviceFilePath;

    public static string DefaultServiceFilePath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        var baseDir = string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".config") : xdg;
        return Path.Combine(baseDir, "systemd", "user", ServiceName);
    }

    public async Task<ServiceInstallState> GetStateAsync(CancellationToken ct)
    {
        if (!File.Exists(_serviceFilePath)) return ServiceInstallState.NotInstalled;
        var (exit, stdout, _) = await RunSystemctlAsync(new[] { "--user", "is-active", ServiceName }, ct)
            .ConfigureAwait(false);
        // is-active: exit 0 means active, otherwise inactive/failed.
        return exit == 0 && stdout.Trim() == "active"
            ? ServiceInstallState.Running
            : ServiceInstallState.Stopped;
    }

    public async Task<ServiceOperationResult> InstallAsync(DaemonConfig config, CancellationToken ct)
    {
        if (!config.IsValidForInstall(out var error))
        {
            return new ServiceOperationResult(false, error ?? "Invalid daemon config.");
        }
        try
        {
            WriteUnitFile(config);
            var reload = await RunSystemctlAsync(new[] { "--user", "daemon-reload" }, ct).ConfigureAwait(false);
            if (reload.Exit != 0)
            {
                return new ServiceOperationResult(false, $"daemon-reload failed: {reload.Stderr.Trim()}");
            }
            var enable = await RunSystemctlAsync(new[] { "--user", "enable", "--now", ServiceName }, ct).ConfigureAwait(false);
            if (enable.Exit != 0)
            {
                return new ServiceOperationResult(false, $"enable --now failed: {enable.Stderr.Trim()}");
            }
            return new ServiceOperationResult(true, "Service installed and started.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Install failed.");
            return new ServiceOperationResult(false, ex.Message);
        }
    }

    public async Task<ServiceOperationResult> UninstallAsync(CancellationToken ct)
    {
        try
        {
            // disable --now stops the service AND removes the symlink in
            // graphical-session.target.wants/. Tolerate failure here — if the
            // unit is already broken/uninstalled, we still want to delete the
            // file and daemon-reload.
            var disable = await RunSystemctlAsync(new[] { "--user", "disable", "--now", ServiceName }, ct).ConfigureAwait(false);
            if (disable.Exit != 0)
            {
                _logger?.LogDebug("disable --now exited {Exit}: {Stderr}", disable.Exit, disable.Stderr);
            }
            if (File.Exists(_serviceFilePath))
            {
                File.Delete(_serviceFilePath);
            }
            var reload = await RunSystemctlAsync(new[] { "--user", "daemon-reload" }, ct).ConfigureAwait(false);
            if (reload.Exit != 0)
            {
                return new ServiceOperationResult(false, $"daemon-reload failed: {reload.Stderr.Trim()}");
            }
            return new ServiceOperationResult(true, "Service stopped and unit file removed.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Uninstall failed.");
            return new ServiceOperationResult(false, ex.Message);
        }
    }

    public async Task<ServiceOperationResult> ReinstallAsync(DaemonConfig config, CancellationToken ct)
    {
        if (!config.IsValidForInstall(out var error))
        {
            return new ServiceOperationResult(false, error ?? "Invalid daemon config.");
        }
        try
        {
            WriteUnitFile(config);
            var reload = await RunSystemctlAsync(new[] { "--user", "daemon-reload" }, ct).ConfigureAwait(false);
            if (reload.Exit != 0)
            {
                return new ServiceOperationResult(false, $"daemon-reload failed: {reload.Stderr.Trim()}");
            }
            // restart picks up the new ExecStart line. Don't toggle enable —
            // either it was enabled (still is) or the user disabled it on
            // purpose (leave alone).
            var restart = await RunSystemctlAsync(new[] { "--user", "restart", ServiceName }, ct).ConfigureAwait(false);
            if (restart.Exit != 0)
            {
                return new ServiceOperationResult(false, $"restart failed: {restart.Stderr.Trim()}");
            }
            return new ServiceOperationResult(true, "Unit rewritten and service restarted.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Reinstall failed.");
            return new ServiceOperationResult(false, ex.Message);
        }
    }

    private void WriteUnitFile(DaemonConfig config)
    {
        var dir = Path.GetDirectoryName(_serviceFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        var content = SystemdUnitFileBuilder.Build(config);
        File.WriteAllText(_serviceFilePath, content);
    }

    private async Task<(int Exit, string Stdout, string Stderr)> RunSystemctlAsync(
        IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("systemctl")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to launch systemctl.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct).ConfigureAwait(false);
        return (p.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }
}
