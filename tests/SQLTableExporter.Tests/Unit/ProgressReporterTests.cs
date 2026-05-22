using Xunit;

namespace SQLTableExporter.Tests.Unit;

public class ProgressReporterTests
{
    [Fact]
    public void Reports_zero_percent_when_no_rows_processed_yet()
    {
        var r = new ProgressReporter
        {
            TotalRows = 1000,
            ProcessedRows = 0,
            CurrentFileNumber = 1,
            StartTime = DateTime.Now
        };

        Assert.Equal("0% complete", r.GetProgressReport());
    }

    [Fact]
    public void Reports_percent_complete_and_file_number_when_partial()
    {
        var r = new ProgressReporter
        {
            TotalRows = 1000,
            ProcessedRows = 250,
            CurrentFileNumber = 3,
            StartTime = DateTime.Now.AddSeconds(-10)
        };

        string report = r.GetProgressReport();

        Assert.Contains("25.00% complete", report);
        Assert.Contains("File 3", report);
        Assert.Contains("Elapsed:", report);
        Assert.Contains("Remaining:", report);
        Assert.Contains("Rows/sec:", report);
    }

    [Fact]
    public void Reports_one_hundred_percent_when_done()
    {
        var r = new ProgressReporter
        {
            TotalRows = 500,
            ProcessedRows = 500,
            CurrentFileNumber = 1,
            StartTime = DateTime.Now.AddSeconds(-5)
        };

        string report = r.GetProgressReport();

        Assert.Contains("100.00% complete", report);
    }
}
