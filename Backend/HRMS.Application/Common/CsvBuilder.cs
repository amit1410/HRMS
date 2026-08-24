using System.Text;

namespace HRMS.Application.Common;

/// <summary>
/// Builds a CSV document for download.
/// <para>
/// Two details matter more than they look. First, a UTF-8 byte-order mark is emitted: without it Excel
/// reads the file in the local ANSI code page and mangles every non-ASCII name. Second, values that begin
/// with a formula character are prefixed with an apostrophe — a cell starting with <c>=</c>, <c>+</c>,
/// <c>-</c> or <c>@</c> is executed as a formula when the file is opened, which turns a text field an
/// employee controls into code running on whoever opens the export (CSV injection). The trade-off is
/// visible: a phone number stored as "+91…" exports as "'+91…". Neutralizing it is the right side of that
/// trade.
/// </para>
/// </summary>
public sealed class CsvBuilder
{
    // RFC 4180 specifies CRLF between records, and it is what spreadsheet software expects on Windows.
    private const string RowSeparator = "\r\n";

    private static readonly char[] FormulaTriggers = ['=', '+', '-', '@', '\t', '\r'];

    private readonly StringBuilder _content = new();

    public CsvBuilder(params string[] headers)
    {
        ColumnCount = headers.Length;
        AppendRow(headers);
        RowCount = 0;
    }

    /// <summary>Number of data rows written, excluding the header.</summary>
    public int RowCount { get; private set; }

    private int ColumnCount { get; }

    /// <summary>Appends one data row. The column count must match the header.</summary>
    public void AppendRow(params string?[] values)
    {
        if (values.Length != ColumnCount)
        {
            throw new ArgumentException(
                $"Expected {ColumnCount} values to match the header, but received {values.Length}.", nameof(values));
        }

        AppendRowCore(values);
        RowCount++;
    }

    public byte[] ToUtf8Bytes()
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(_content.ToString());

        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    private void AppendRowCore(IReadOnlyList<string?> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                _content.Append(',');
            }

            _content.Append(EscapeField(values[i]));
        }

        _content.Append(RowSeparator);
    }

    private static string EscapeField(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var field = value;

        if (FormulaTriggers.Contains(field[0]))
        {
            field = "'" + field;
        }

        // Quote when the value contains a delimiter, a quote or a line break, and when it has leading or
        // trailing whitespace that would otherwise be silently trimmed by some readers.
        var needsQuoting =
            field.Contains(',') ||
            field.Contains('"') ||
            field.Contains('\n') ||
            field.Contains('\r') ||
            char.IsWhiteSpace(field[0]) ||
            char.IsWhiteSpace(field[^1]);

        return needsQuoting
            ? '"' + field.Replace("\"", "\"\"") + '"'
            : field;
    }
}
