namespace HRMS.Application.DTOs.Cities;

public class CityRequest
{
    public Guid StateId { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}
