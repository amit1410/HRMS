using HRMS.Application.Common;

namespace HRMS.Application.DTOs.States;

public class StateQuery : PagedQuery
{
    public Guid? CountryId { get; set; }

    public bool? IsActive { get; set; }

    public static readonly IReadOnlyList<string> SortFields =
    [
        "code", "name", "countryId", "isActive", "createdDate"
    ];
}
