using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Cities;

public class CityQuery : PagedQuery
{
    public Guid? StateId { get; set; }

    public bool? IsActive { get; set; }

    public static readonly IReadOnlyList<string> SortFields =
    [
        "code", "name", "stateId", "isActive", "createdDate"
    ];
}
