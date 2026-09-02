using System.Globalization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;

namespace HRMS.Application.EmployeeCodes;

public sealed class EmployeeCodeRenderer
{
    public (string? Code, string? Error) Render(EmployeeCodeRule rule, EmployeeCodeGenerationContext context, long sequence, string separator = "-")
    {
        var parts = new List<string>();
        foreach (var segment in rule.Segments.OrderBy(s => s.SequenceOrder))
        {
            string? value = segment.SegmentType switch
            {
                EmployeeCodeSegmentType.SequentialNumber => sequence.ToString(segment.PaddingLength is > 0 ? $"D{segment.PaddingLength}" : "0", CultureInfo.InvariantCulture),
                EmployeeCodeSegmentType.FixedText or EmployeeCodeSegmentType.CustomConstant => segment.FixedValue,
                EmployeeCodeSegmentType.JoiningYear => context.JoiningDate.Year.ToString(CultureInfo.InvariantCulture),
                EmployeeCodeSegmentType.JoiningMonth => context.JoiningDate.Month.ToString("D2", CultureInfo.InvariantCulture),
                _ => context.SegmentValues.TryGetValue(segment.SegmentType, out var masterCode) ? masterCode : null
            };
            if (string.IsNullOrWhiteSpace(value)) return (null, $"Employee Code cannot be generated because {segment.SegmentType} has no configured Code.");
            parts.Add(value.Trim());
        }
        return parts.Count == 0 ? (null, "Employee Code rule has no usable segments.") : (string.Join(separator ?? "-", parts), null);
    }
}
