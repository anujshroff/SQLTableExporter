namespace SQLTableExporter;

/// <summary>
/// Handles export progress tracking and reporting
/// </summary>
public class ProgressReporter
{
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int CurrentFileNumber { get; set; }
    public DateTime StartTime { get; set; }

    public string GetProgressReport()
    {
        double percentComplete = (double)ProcessedRows / TotalRows * 100;
        TimeSpan elapsed = DateTime.Now - StartTime;

        if (ProcessedRows == 0)
        {
            return "0% complete";
        }

        double rowsPerSecond = ProcessedRows / elapsed.TotalSeconds;
        int remainingRows = TotalRows - ProcessedRows;
        TimeSpan estimatedRemainingTime = TimeSpan.FromSeconds(remainingRows / rowsPerSecond);

        return $"{percentComplete:F2}% complete | " +
               $"File {CurrentFileNumber} | " +
               $"Elapsed: {FormatTimeSpan(elapsed)} | " +
               $"Remaining: {FormatTimeSpan(estimatedRemainingTime)} | " +
               $"Rows/sec: {rowsPerSecond:F0}";
    }

    private static string FormatTimeSpan(TimeSpan timeSpan) => timeSpan.TotalHours >= 1
            ? $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m"
            : timeSpan.TotalMinutes >= 1 ? $"{(int)timeSpan.TotalMinutes}m {timeSpan.Seconds}s" : $"{(int)timeSpan.TotalSeconds}s";
}
