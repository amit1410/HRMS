using System.Text;
using HRMS.Application.Common;

namespace HRMS.Tests;

public sealed class PayrollReportsCsvTests
{
    [Fact]
    public void Csv_builder_preserves_safe_escaping_and_unicode()
    {
        var csv = new CsvBuilder("Value");
        csv.AppendRow("Smith, John");
        csv.AppendRow("He said \"check\"");
        csv.AppendRow("Line 1\r\nLine 2");
        csv.AppendRow("Amit कुमार");
        var text = Encoding.UTF8.GetString(csv.ToUtf8Bytes());
        Assert.Contains("\"Smith, John\"", text);
        Assert.Contains("\"He said \"\"check\"\"\"", text);
        Assert.Contains("\"Line 1\r\nLine 2\"", text);
        Assert.Contains("Amit कुमार", text);
    }
}
