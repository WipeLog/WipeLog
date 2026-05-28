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

using System.Text;
using System.Threading.Channels;
using Wipelog.Core.CombatLog;
using Xunit;

namespace Wipelog.Core.Tests;

public class LogTailerTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly DateTimeOffset ClockNow = new(2024, 11, 9, 20, 0, 0, TimeSpan.Zero);

    private static string Line(string body) => body + "\n";

    private static async Task AppendAsync(string path, string content)
    {
        // Use FileShare.ReadWrite|Delete on the writer so the tailer can read while we write.
        await using var fs = new FileStream(
            path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        var bytes = Encoding.UTF8.GetBytes(content);
        await fs.WriteAsync(bytes);
    }

    private static async Task<CombatLogEvent> ReadOneAsync(ChannelReader<CombatLogEvent> reader)
    {
        using var cts = new CancellationTokenSource(ReadTimeout);
        return await reader.ReadAsync(cts.Token);
    }

    private static async Task<bool> NoEventForAsync(ChannelReader<CombatLogEvent> reader, TimeSpan window)
    {
        using var cts = new CancellationTokenSource(window);
        try
        {
            await reader.ReadAsync(cts.Token);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    [Fact]
    public async Task Emits_event_when_complete_line_is_appended()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "log.txt");
        File.WriteAllText(path, string.Empty);

        var parser = new CombatLogParser(new FakeClock(ClockNow));
        await using var tailer = new LogTailer(path, parser);
        await tailer.StartAsync(CancellationToken.None);

        await AppendAsync(path, Line("11/9 19:43:01.122  ENCOUNTER_START,2902,\"Ulgrax the Devourer\",16,20,2657"));

        var evt = await ReadOneAsync(tailer.Events);
        var es = Assert.IsType<EncounterStart>(evt);
        Assert.Equal(2902, es.EncounterId);
    }

    [Fact]
    public async Task Buffers_partial_line_until_newline_arrives()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "log.txt");
        File.WriteAllText(path, string.Empty);

        var parser = new CombatLogParser(new FakeClock(ClockNow));
        await using var tailer = new LogTailer(path, parser);
        await tailer.StartAsync(CancellationToken.None);

        await AppendAsync(path, "11/9 19:43:01.122  ENCOUNTER_START,2902,\"Ulgrax the Devourer\"");
        Assert.True(await NoEventForAsync(tailer.Events, TimeSpan.FromMilliseconds(400)));

        await AppendAsync(path, ",16,20,2657\n");

        var evt = await ReadOneAsync(tailer.Events);
        var es = Assert.IsType<EncounterStart>(evt);
        Assert.Equal("Ulgrax the Devourer", es.EncounterName);
    }

    [Fact]
    public async Task Resets_offset_on_truncation_and_reads_replacement_content()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "log.txt");
        File.WriteAllText(path, string.Empty);

        var parser = new CombatLogParser(new FakeClock(ClockNow));
        await using var tailer = new LogTailer(path, parser);
        await tailer.StartAsync(CancellationToken.None);

        await AppendAsync(path, Line("11/9 19:43:01.122  ENCOUNTER_START,2902,\"A\",16,20,2657"));
        var first = Assert.IsType<EncounterStart>(await ReadOneAsync(tailer.Events));
        Assert.Equal(2902, first.EncounterId);

        // Truncate the file (simulates a new WoW session). Wait for the
        // truncation FSW event to propagate before writing new content,
        // otherwise the test races the watcher and the empty state is never
        // observed by the tailer.
        await using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                         FileShare.ReadWrite | FileShare.Delete))
        {
            // intentionally empty after Create
        }
        await Task.Delay(300, TestContext.Current.CancellationToken);

        await AppendAsync(path, Line("11/9 19:50:00.000  ENCOUNTER_START,3001,\"B\",15,20,2657"));

        var second = Assert.IsType<EncounterStart>(await ReadOneAsync(tailer.Events));
        Assert.Equal(3001, second.EncounterId);
    }

    [Fact]
    public async Task Emits_multiple_lines_in_order_from_one_append()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "log.txt");
        File.WriteAllText(path, string.Empty);

        var parser = new CombatLogParser(new FakeClock(ClockNow));
        await using var tailer = new LogTailer(path, parser);
        await tailer.StartAsync(CancellationToken.None);

        var batch =
            Line("11/9 19:43:01.122  ENCOUNTER_START,2902,\"X\",16,20,2657") +
            Line("11/9 19:47:55.890  ENCOUNTER_END,2902,\"X\",16,20,1") +
            Line("11/9 19:48:00.000  UNIT_DIED,0000000000000000,nil,0x80000000,0x80000000,Player-1-A,\"Foo\",0x511,0x0");
        await AppendAsync(path, batch);

        var first = await ReadOneAsync(tailer.Events);
        var second = await ReadOneAsync(tailer.Events);
        var third = await ReadOneAsync(tailer.Events);

        Assert.IsType<EncounterStart>(first);
        Assert.IsType<EncounterEnd>(second);
        Assert.IsType<UnitDied>(third);
    }

    [Fact]
    public async Task Directory_mode_picks_up_newly_created_file_and_reads_from_start()
    {
        using var dir = new TempDirectory();
        // Pre-existing file from a "previous session" — should NOT be replayed.
        var oldFile = Path.Combine(dir.Path, "WoWCombatLog-old.txt");
        File.WriteAllText(oldFile,
            Line("11/9 18:00:00.000  ENCOUNTER_START,1111,\"Old\",16,20,2657"));

        var parser = new CombatLogParser(new FakeClock(ClockNow));
        await using var tailer = new LogTailer(dir.Path, "WoWCombatLog-*.txt", parser);
        await tailer.StartAsync(CancellationToken.None);

        // Old content tailed from end → no events.
        Assert.True(await NoEventForAsync(tailer.Events, TimeSpan.FromMilliseconds(400)));

        // Simulate WoW creating a brand new session file.
        var newFile = Path.Combine(dir.Path, "WoWCombatLog-new.txt");
        await File.WriteAllTextAsync(newFile,
            Line("11/9 19:00:00.000  CHALLENGE_MODE_START,\"The Stonevault\",2290,505,12,[9,135,136,148]") +
            Line("11/9 19:00:01.000  ENCOUNTER_START,3000,\"Boss\",16,20,2657"),
            TestContext.Current.CancellationToken);

        // Make sure the new file is detectably newer than the old one.
        File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow);

        var first = await ReadOneAsync(tailer.Events);
        Assert.IsType<ChallengeModeStart>(first);
        var second = await ReadOneAsync(tailer.Events);
        Assert.IsType<EncounterStart>(second);
    }

    [Fact]
    public async Task Directory_mode_with_no_existing_files_still_starts_cleanly()
    {
        using var dir = new TempDirectory();
        var parser = new CombatLogParser(new FakeClock(ClockNow));
        await using var tailer = new LogTailer(dir.Path, "WoWCombatLog-*.txt", parser);
        await tailer.StartAsync(CancellationToken.None);

        // No existing file at all. When one appears, it should be tailed from byte 0.
        var newFile = Path.Combine(dir.Path, "WoWCombatLog-fresh.txt");
        await File.WriteAllTextAsync(newFile,
            Line("11/9 19:00:00.000  ENCOUNTER_START,2902,\"Fresh\",16,20,2657"),
            TestContext.Current.CancellationToken);

        var evt = await ReadOneAsync(tailer.Events);
        var es = Assert.IsType<EncounterStart>(evt);
        Assert.Equal(2902, es.EncounterId);
    }

    [Fact]
    public async Task Skips_existing_content_at_start_then_emits_new_appends()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "log.txt");
        File.WriteAllText(path,
            Line("11/9 18:00:00.000  ENCOUNTER_START,1111,\"Pre\",16,20,2657") +
            Line("11/9 18:05:00.000  ENCOUNTER_END,1111,\"Pre\",16,20,1"));

        var parser = new CombatLogParser(new FakeClock(ClockNow));
        await using var tailer = new LogTailer(path, parser);
        await tailer.StartAsync(CancellationToken.None);

        Assert.True(await NoEventForAsync(tailer.Events, TimeSpan.FromMilliseconds(400)));

        await AppendAsync(path, Line("11/9 19:00:00.000  ENCOUNTER_START,2222,\"New\",16,20,2657"));
        var es = Assert.IsType<EncounterStart>(await ReadOneAsync(tailer.Events));
        Assert.Equal(2222, es.EncounterId);
    }
}
