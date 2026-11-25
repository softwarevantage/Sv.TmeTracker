using System.Globalization;
using System.IO;
using System.Windows;
using Sv.TimeTracker.Models;
using Sv.TimeTracker.Services;
using Sv.TimeTracker.Tests.TestInfrastructure;
using Xunit;

namespace Sv.TimeTracker.Tests.Services;

public class ExportAndFormattingTests
{
    [Fact]
    public void DurationFormatter_FormatsMultiDaySpansWithoutWrappingAt24Hours()
    {
        var span = new TimeSpan(days: 1, hours: 3, minutes: 15, seconds: 5);
        Assert.Equal("27:15:05", DurationFormatter.Format(span));
        Assert.Equal("00:00:00", DurationFormatter.Format(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void BuildCsv_EscapesCommasQuotesAndMultilineNotesPerRfc4180()
    {
        var exportService = new ExportService();
        var entries = new[]
        {
            new EnrichedTimeEntry
            {
                Id = 1,
                WorkItemId = 10,
                WorkItemTitle = "Client \"Alpha\", Phase 2",
                StartTimeUtc = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc),
                EndTimeUtc = new DateTime(2026, 11, 24, 16, 30, 0, DateTimeKind.Utc),
                DurationMs = 27 * 3_600_000L + 15 * 60_000L,
                Notes = "Line 1, with comma\nLine 2 \"quoted\""
            }
        };

        var csv = exportService.BuildCsv(entries);
        Assert.Contains("Date,Start Time,End Time,Duration,DurationHours,Work Item,Notes", csv);
        Assert.Contains("27:15:00,27.25,\"Client \"\"Alpha\"\", Phase 2\",\"Line 1, with comma\nLine 2 \"\"quoted\"\"\"", csv);
    }

    [Fact]
    public async Task ExportTimeEntriesAsync_WritesUtf8CsvFileToDisk()
    {
        using var db = new TempDatabaseFixture(seedStarterWorkItems: false);
        var item = await db.WorkItems.CreateAsync(new WorkItem { Title = "Release Prep" });

        var nowUtc = DateTime.UtcNow;
        await db.TimeEntries.CreateAsync(new TimeEntry
        {
            WorkItemId = item.Id,
            StartTime = nowUtc.AddMinutes(-30),
            EndTime = nowUtc,
            DurationMs = 1_800_000,
            Notes = "Verified build, tests, and installer"
        });

        var service = new ExportService(db.TimeEntries);
        var csvPath = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "export.csv");

        var result = await service.ExportTimeEntriesAsync(null, null, null, csvPath);
        Assert.True(result.Success);
        Assert.Equal(1, result.ExportedCount);
        Assert.True(File.Exists(csvPath));

        var contents = await File.ReadAllTextAsync(csvPath);
        Assert.Contains("Release Prep", contents);
        Assert.Contains("\"Verified build, tests, and installer\"", contents);
    }

    [Fact]
    public void ValueConverters_HandleStringsBooleansInverseAndInvalidHexSafely()
    {
        var visibilityConverter = new BooleanToVisibilityConverter();
        Assert.Equal(Visibility.Visible, visibilityConverter.Convert(true, typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, visibilityConverter.Convert(true, typeof(Visibility), "Inverse", CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Visible, visibilityConverter.Convert("Non-empty note", typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, visibilityConverter.Convert("   ", typeof(Visibility), null, CultureInfo.InvariantCulture));
        Assert.Equal(Visibility.Collapsed, visibilityConverter.Convert(null, typeof(Visibility), null, CultureInfo.InvariantCulture));

        var hexConverter = new HexColorToBrushConverter();
        Assert.NotNull(hexConverter.Convert("#10B981", typeof(object), null, CultureInfo.InvariantCulture));
        Assert.NotNull(hexConverter.Convert("not-a-valid-color", typeof(object), null, CultureInfo.InvariantCulture));
    }
}
