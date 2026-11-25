using System.Text;
using System.Windows.Input;
using Moq;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;
using Sv.TimeTracker.Tests.TestInfrastructure;
using Xunit;

namespace Sv.TimeTracker.Tests.Services;

public class NotificationAndHotkeyTests
{
    [Fact]
    public async Task NotificationService_ChimesOncePerSlotAndHonorsQuietHours()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "Coding" });

        // Fix timezone to UTC so local time == UTC time deterministically
        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 11, 24, 14, 0, 5, TimeSpan.Zero),
            TimeZoneInfo.Utc);

        using var timer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        var audioMock = new Mock<IAudioChimePlayer>();
        var notification = new NotificationService(timer, db.Settings, audioMock.Object, clock);

        // Does not chime while timer is stopped
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(It.IsAny<int>(), It.IsAny<string?>()), Times.Never);

        // Start timer at 14:00:05 -> chimes once
        await timer.StartAsync(item.Id);
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(60, string.Empty), Times.Once);

        // Advance 20 seconds still in 14:00 -> must NOT chime a second time!
        clock.Advance(TimeSpan.FromSeconds(20));
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(It.IsAny<int>(), It.IsAny<string?>()), Times.Once);

        // Advance to 14:30 -> chimes a second time
        clock.SetUtcNow(new DateTimeOffset(2026, 11, 24, 14, 30, 0, TimeSpan.Zero));
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(60, string.Empty), Times.Exactly(2));

        // Advance to 23:00 inside default quiet hours (22:00 - 07:00) -> suppressed
        clock.SetUtcNow(new DateTimeOffset(2026, 11, 24, 23, 0, 0, TimeSpan.Zero));
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(It.IsAny<int>(), It.IsAny<string?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task NotificationService_SkipsHalfHourChime_WhenHalfHourDisabled()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "Focus" });
        await db.Settings.SetAsync(SettingKeys.HalfHourDingEnabled, "false");

        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 11, 24, 14, 30, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc);

        using var timer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        var audioMock = new Mock<IAudioChimePlayer>();
        var notification = new NotificationService(timer, db.Settings, audioMock.Object, clock);

        await timer.StartAsync(item.Id);

        // 14:30 should be skipped when HalfHourDingEnabled is false
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(It.IsAny<int>(), It.IsAny<string?>()), Times.Never);

        // 15:00 should still chime
        clock.SetUtcNow(new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero));
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(60, string.Empty), Times.Once);
    }

    [Fact]
    public async Task NotificationService_ChimesWhenStopped_WhenChimeWhenTimerStoppedEnabled()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        await db.Settings.SetAsync(SettingKeys.ChimeWhenTimerStopped, "true");

        var clock = new AdjustableTimeProvider(
            new DateTimeOffset(2026, 11, 24, 16, 0, 0, TimeSpan.Zero),
            TimeZoneInfo.Utc);

        using var timer = new TimerService(db.TimeEntries, db.WorkItems, clock, enableAutoTick: false, db.Settings);
        var audioMock = new Mock<IAudioChimePlayer>();
        var notification = new NotificationService(timer, db.Settings, audioMock.Object, clock);

        Assert.False(timer.IsRunning);
        await notification.CheckHourlyDingAsync();
        audioMock.Verify(a => a.PlayChime(60, string.Empty), Times.Once);
    }

    [Fact]
    public void SynthesizedAudioChimePlayer_BuildsValidRiffWaveStream()
    {
        using var wav = SynthesizedAudioChimePlayer.BuildTwoToneChimeWav(0.75);
        Assert.True(wav.Length > 1000);

        var header = new byte[12];
        var read = wav.Read(header, 0, header.Length);
        Assert.Equal(12, read);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(header, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(header, 8, 4));
    }

    [Theory]
    [InlineData("Ctrl+Shift+T", ModifierKeys.Control | ModifierKeys.Shift, Key.T, "Ctrl+Shift+T")]
    [InlineData("control + alt + f9", ModifierKeys.Control | ModifierKeys.Alt, Key.F9, "Ctrl+Alt+F9")]
    [InlineData("Alt+1", ModifierKeys.Alt, Key.D1, "Alt+1")]
    public void HotkeyParser_ParsesAndNormalizesValidShortcuts(
        string raw,
        ModifierKeys expectedMods,
        Key expectedKey,
        string expectedDisplay)
    {
        var ok = HotkeyParser.TryParse(raw, out var binding);
        Assert.True(ok);
        Assert.Equal(expectedMods, binding.Modifiers);
        Assert.Equal(expectedKey, binding.Key);
        Assert.Equal(expectedDisplay, binding.DisplayText);
        Assert.True(binding.VirtualKey > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl+T+H")]
    [InlineData("Ctrl+InvalidKeyName")]
    public void HotkeyParser_RejectsInvalidShortcuts(string invalid)
    {
        Assert.False(HotkeyParser.TryParse(invalid, out _));
    }
}
