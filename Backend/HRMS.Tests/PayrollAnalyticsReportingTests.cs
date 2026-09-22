using System.Text;
using HRMS.Application.Common;

namespace HRMS.Tests;

public sealed class PayrollAnalyticsReportingTests
{
    [Fact]
    public void Csv_builder_uses_rfc_escaping_and_preserves_header_order()
    {
        var csv = new CsvBuilder("Employee", "Comment", "Note");
        csv.AppendRow("Smith, John", "He said \"check\"", "line one\r\nline two");

        var text = Encoding.UTF8.GetString(csv.ToUtf8Bytes());
        Assert.StartsWith("\uFEFFEmployee,Comment,Note\r\n", text);
        Assert.Contains("\"Smith, John\",\"He said \"\"check\"\"\",\"line one\r\nline two\"\r\n", text);
        Assert.Equal(1, csv.RowCount);
    }

    [Fact]
    public void Csv_builder_emits_header_only_for_empty_result()
    {
        var text = Encoding.UTF8.GetString(new CsvBuilder("Code", "Amount").ToUtf8Bytes());
        Assert.Equal("\uFEFFCode,Amount\r\n", text);
    }
}
