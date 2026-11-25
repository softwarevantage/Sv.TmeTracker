using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using Sv.TimeTracker.Data.Repositories;
using Sv.TimeTracker.Models;

namespace Sv.TimeTracker.Services;

public record ExportResult(
    bool Success,
    int ExportedCount,
    string? FilePath = null,
    string? ErrorMessage = null,
    bool Cancelled = false);

public interface IExportService
{
    string BuildCsv(IEnumerable<EnrichedTimeEntry> entries);
    Task<ExportResult> ExportTimeEntriesAsync(
        DateTime? localStartDate,
        DateTime? localEndDate,
        long? workItemId,
        string? destinationFilePath = null);
}

public sealed class ExportService : IExportService
{
    private readonly ITimeEntryRepository _timeEntryRepo;

    public ExportService(ITimeEntryRepository? timeEntryRepo = null)
    {
        _timeEntryRepo = timeEntryRepo ?? new TimeEntryRepository();
    }

    public string BuildCsv(IEnumerable<EnrichedTimeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var sb = new StringBuilder();
        sb.AppendLine("Date,Start Time,End Time,Duration,DurationHours,Work Item,Notes");

        foreach (var entry in entries)
        {
            var localStart = entry.LocalStartTime;
            var date = localStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var startTime = localStart.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            var endTime = entry.LocalEndTime.HasValue
                ? entry.LocalEndTime.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : "Running";
            var duration = DurationFormatter.FormatMilliseconds(entry.DurationMs);
            var hours = (Math.Max(0, entry.DurationMs) / 3_600_000.0).ToString("F2", CultureInfo.InvariantCulture);
            var workItem = EscapeCsvField(entry.WorkItemTitle);
            var notes = EscapeCsvField(entry.Notes ?? string.Empty);

            sb.Append(date).Append(',')
              .Append(startTime).Append(',')
              .Append(endTime).Append(',')
              .Append(duration).Append(',')
              .Append(hours).Append(',')
              .Append(workItem).Append(',')
              .Append(notes)
              .AppendLine();
        }

        return sb.ToString();
    }

    public async Task<ExportResult> ExportTimeEntriesAsync(
        DateTime? localStartDate,
        DateTime? localEndDate,
        long? workItemId,
        string? destinationFilePath = null)
    {
        try
        {
            var entries = await _timeEntryRepo.GetEnrichedEntriesAsync(localStartDate, localEndDate, workItemId);
            if (entries.Count == 0)
            {
                return new ExportResult(
                    Success: false,
                    ExportedCount: 0,
                    ErrorMessage: "No time entries match the selected filters.");
            }

            var targetPath = destinationFilePath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    FileName = $"SvTimeTracker_Export_{DateTime.Now:yyyyMMdd_HHmm}.csv"
                };

                if (dialog.ShowDialog() != true)
                {
                    return new ExportResult(Success: false, ExportedCount: 0, Cancelled: true);
                }

                targetPath = dialog.FileName;
            }

            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var csv = BuildCsv(entries);
            await File.WriteAllTextAsync(targetPath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            return new ExportResult(Success: true, ExportedCount: entries.Count, FilePath: targetPath);
        }
        catch (Exception ex)
        {
            return new ExportResult(Success: false, ExportedCount: 0, ErrorMessage: ex.Message);
        }
    }

    public static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var requiresQuotes = value.IndexOfAny([',', '"', '\r', '\n']) >= 0
                             || char.IsWhiteSpace(value[0])
                             || char.IsWhiteSpace(value[^1]);

        if (!requiresQuotes)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
