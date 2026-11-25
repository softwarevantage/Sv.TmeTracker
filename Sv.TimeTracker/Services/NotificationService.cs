using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Services;

public interface IAudioChimePlayer
{
    void PlayChime(int volumePercent, string? customFilePath = null);
}

public sealed class SynthesizedAudioChimePlayer : IAudioChimePlayer
{
    private MediaPlayer? _mediaPlayer;

    public void PlayChime(int volumePercent, string? customFilePath = null)
    {
        var clampedVolume = Math.Clamp(volumePercent, 0, 100);
        if (clampedVolume <= 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(customFilePath) && File.Exists(customFilePath))
        {
            PlayCustomFile(customFilePath, clampedVolume / 100.0);
            return;
        }

        Task.Run(() =>
        {
            try
            {
                using var wavStream = BuildTwoToneChimeWav(clampedVolume / 100.0);
                using var player = new SoundPlayer(wavStream);
                player.PlaySync();
            }
            catch
            {
                // Ignore audio device errors on headless machines
            }
        });
    }

    private void PlayCustomFile(string filePath, double normalizedVolume)
    {
        void PlayOnDispatcher()
        {
            try
            {
                _mediaPlayer ??= new MediaPlayer();
                _mediaPlayer.Stop();
                _mediaPlayer.Open(new Uri(filePath, UriKind.Absolute));
                _mediaPlayer.Volume = normalizedVolume;
                _mediaPlayer.Play();
            }
            catch
            {
                // Fallback to synthesized chime if file cannot be decoded
                using var wavStream = BuildTwoToneChimeWav(normalizedVolume);
                using var player = new SoundPlayer(wavStream);
                player.Play();
            }
        }

        if (Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(PlayOnDispatcher);
        }
        else
        {
            PlayOnDispatcher();
        }
    }

    /// <summary>
    /// Generates a pleasant 16-bit 44.1kHz stereo/mono harmonic C5 -> G5 chime in memory.
    /// </summary>
    public static MemoryStream BuildTwoToneChimeWav(double amplitudeScale)
    {
        const int sampleRate = 44100;
        const double durationSeconds = 0.55;
        var totalSamples = (int)(sampleRate * durationSeconds);
        var dataBytes = totalSamples * sizeof(short);

        var stream = new MemoryStream(44 + dataBytes);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(36 + dataBytes);
            writer.Write("WAVE"u8);
            writer.Write("fmt "u8);
            writer.Write(16); // PCM chunk size
            writer.Write((short)1); // PCM format
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16); // 16-bit
            writer.Write("data"u8);
            writer.Write(dataBytes);

            var gain = Math.Clamp(amplitudeScale, 0.0, 1.0) * 0.45;
            var secondNoteStart = (int)(sampleRate * 0.16);

            for (var i = 0; i < totalSamples; i++)
            {
                var t = (double)i / sampleRate;
                // First note: E5 (659.25 Hz)
                var env1 = Math.Exp(-t * 8.0) * Math.Min(1.0, t / 0.008);
                var sample = env1 * (Math.Sin(2 * Math.PI * 659.25 * t) + 0.3 * Math.Sin(2 * Math.PI * 1318.5 * t));

                // Second note: B5 (987.77 Hz)
                if (i >= secondNoteStart)
                {
                    var t2 = (double)(i - secondNoteStart) / sampleRate;
                    var env2 = Math.Exp(-t2 * 6.5) * Math.Min(1.0, t2 / 0.008);
                    sample += env2 * (Math.Sin(2 * Math.PI * 987.77 * t2) + 0.25 * Math.Sin(2 * Math.PI * 1975.5 * t2));
                }

                var pcm = (short)Math.Clamp(sample * gain * short.MaxValue, short.MinValue, short.MaxValue);
                writer.Write(pcm);
            }
        }

        stream.Position = 0;
        return stream;
    }
}

public interface INotificationService
{
    Task CheckHourlyDingAsync();
    void PlayTestChime(int volumePercent, string? customFilePath = null);
}

public sealed class NotificationService : INotificationService
{
    private readonly ITimerService _timerService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IAudioChimePlayer _audioPlayer;
    private readonly TimeProvider _timeProvider;
    private DateTime? _lastChimeMinuteSlot;

    public NotificationService(
        ITimerService timerService,
        ISettingsRepository? settingsRepo = null,
        IAudioChimePlayer? audioPlayer = null,
        TimeProvider? timeProvider = null)
    {
        _timerService = timerService ?? throw new ArgumentNullException(nameof(timerService));
        _settingsRepo = settingsRepo ?? new SettingsRepository();
        _audioPlayer = audioPlayer ?? new SynthesizedAudioChimePlayer();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task CheckHourlyDingAsync()
    {
        var localNow = _timeProvider.GetLocalNow().DateTime;
        if (localNow.Minute is not (0 or 30))
        {
            return;
        }

        var currentSlot = new DateTime(
            localNow.Year,
            localNow.Month,
            localNow.Day,
            localNow.Hour,
            localNow.Minute,
            0,
            DateTimeKind.Local);

        if (_lastChimeMinuteSlot.HasValue && _lastChimeMinuteSlot.Value == currentSlot)
        {
            return;
        }

        var enabled = await _settingsRepo.GetBoolAsync(SettingKeys.HourlyDingEnabled, true);
        if (!enabled)
        {
            return;
        }

        if (!_timerService.IsRunning)
        {
            var chimeWhenStopped = await _settingsRepo.GetBoolAsync(SettingKeys.ChimeWhenTimerStopped, false);
            if (!chimeWhenStopped)
            {
                return;
            }
        }

        if (localNow.Minute == 30)
        {
            var halfHourEnabled = await _settingsRepo.GetBoolAsync(SettingKeys.HalfHourDingEnabled, true);
            if (!halfHourEnabled)
            {
                return;
            }
        }

        var volume = await _settingsRepo.GetIntAsync(SettingKeys.HourlyDingVolume, 60);
        if (volume <= 0)
        {
            return;
        }

        var quietStart = await _settingsRepo.GetAsync(SettingKeys.QuietHoursStart);
        var quietEnd = await _settingsRepo.GetAsync(SettingKeys.QuietHoursEnd);
        if (IsWithinQuietHours(localNow.TimeOfDay, quietStart, quietEnd))
        {
            return;
        }

        _lastChimeMinuteSlot = currentSlot;
        var soundPath = await _settingsRepo.GetAsync(SettingKeys.HourlyDingSoundPath);
        _audioPlayer.PlayChime(volume, soundPath);
    }

    public void PlayTestChime(int volumePercent, string? customFilePath = null)
    {
        _audioPlayer.PlayChime(volumePercent, customFilePath);
    }

    public static bool IsWithinQuietHours(TimeSpan currentTimeOfDay, string? quietStartRaw, string? quietEndRaw)
    {
        if (string.IsNullOrWhiteSpace(quietStartRaw) || string.IsNullOrWhiteSpace(quietEndRaw))
        {
            return false;
        }

        if (!TimeSpan.TryParse(quietStartRaw, out var start) || !TimeSpan.TryParse(quietEndRaw, out var end))
        {
            return false;
        }

        if (start == end)
        {
            return false;
        }

        return start > end
            ? currentTimeOfDay >= start || currentTimeOfDay < end
            : currentTimeOfDay >= start && currentTimeOfDay < end;
    }
}
