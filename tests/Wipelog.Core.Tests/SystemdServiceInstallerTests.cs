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

using Wipelog.App.Services;
using Wipelog.Core.Preferences;
using Xunit;

namespace Wipelog.Core.Tests;

public class SystemdServiceInstallerTests
{
    // Verifying the file-writing behavior without launching `systemctl`. The
    // production installer also calls systemctl after the write — those are
    // exercised by manual verification (packaging/README.md), since spawning
    // a real systemctl process from a test is brittle.
    //
    // We invoke the public Build helper directly instead of going through
    // InstallAsync (which would also touch systemctl). That keeps the test
    // fast and deterministic.

    [Fact]
    public void Unit_file_round_trips_a_valid_config_through_the_builder()
    {
        var c = DaemonConfig.WithHomeDefaults() with
        {
            LogDirectory = "/home/u/Logs",
            DaemonExecutablePath = "/opt/wowrec/Wipelog.Daemon",
            OutputDirectory = "/data/wow",
        };
        var content = SystemdUnitFileBuilder.Build(c);
        Assert.Contains("ExecStart=\"/opt/wowrec/Wipelog.Daemon\"", content);
        Assert.Contains("--log-dir \"/home/u/Logs\"", content);
        Assert.Contains("--output \"/data/wow\"", content);
    }

    [Fact]
    public void DefaultServiceFilePath_points_under_systemd_user()
    {
        var path = SystemdServiceInstaller.DefaultServiceFilePath();
        Assert.EndsWith("/systemd/user/wipelog.service", path.Replace('\\', '/'));
    }

    [Fact]
    public async Task GetStateAsync_returns_NotInstalled_when_file_missing()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "wipelog.service");
        var installer = new SystemdServiceInstaller(path);
        var state = await installer.GetStateAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ServiceInstallState.NotInstalled, state);
    }

    [Fact]
    public async Task InstallAsync_rejects_invalid_config()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "wipelog.service");
        var installer = new SystemdServiceInstaller(path);
        var result = await installer.InstallAsync(new DaemonConfig(), TestContext.Current.CancellationToken);
        Assert.False(result.Success);
        Assert.False(File.Exists(path));
    }
}
