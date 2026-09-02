using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Countries;

public class CountryQuery : PagedQuery
{
    public bool? IsActive { get; set; }

    public static readonly IReadOnlyList<string> SortFields =
    [
        "code", "name", "isActive", "createdDate"
    ];
}
