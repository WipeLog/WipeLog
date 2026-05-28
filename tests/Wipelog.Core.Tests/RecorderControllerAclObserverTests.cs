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

using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Wipelog.Core.CombatLog;
using Wipelog.Core.Detection;
using Wipelog.Core.Recording;
using Xunit;

namespace Wipelog.Core.Tests;

public class RecorderControllerAclObserverTests
{
    private sealed class CapturingObserver : IAdvancedLoggingObserver
    {
        public List<CombatLogVersion> Calls { get; } = new();
        public void OnCombatLogVersion(CombatLogVersion ev) => Calls.Add(ev);
    }

    private static DateTimeOffset T(int hour, int minute = 0, int second = 0) =>
        new(2026, 5, 19, hour, minute, second, TimeSpan.Zero);

    [Fact]
    public async Task Combat_log_version_events_are_forwarded_to_the_observer()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var observer = new CapturingObserver();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            aclObserver: observer);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new CombatLogVersion(T(12, 0), 21, AdvancedLogging: false),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new CombatLogVersion(T(13, 0), 21, AdvancedLogging: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        Assert.Equal(2, observer.Calls.Count);
        Assert.False(observer.Calls[0].AdvancedLogging);
        Assert.True(observer.Calls[1].AdvancedLogging);
    }

    [Fact]
    public async Task Non_version_events_do_not_invoke_the_observer()
    {
        using var dir = new TempDirectory();
        var stub = new StubRecorder();
        var observer = new CapturingObserver();
        var controller = new RecorderController(
            new EncounterDetector(), stub, new DefaultClipNamingStrategy(),
            dir.Path, NullLogger<RecorderController>.Instance,
            aclObserver: observer);

        var channel = Channel.CreateUnbounded<CombatLogEvent>();
        await channel.Writer.WriteAsync(
            new EncounterStart(T(12, 0), 2902, "Ulgrax", 16, 20, 2657),
            TestContext.Current.CancellationToken);
        await channel.Writer.WriteAsync(
            new EncounterEnd(T(12, 5), 2902, "Ulgrax", 16, 20, Success: true),
            TestContext.Current.CancellationToken);
        channel.Writer.Complete();

        await controller.RunAsync(channel.Reader, TestContext.Current.CancellationToken);

        Assert.Empty(observer.Calls);
    }
}
