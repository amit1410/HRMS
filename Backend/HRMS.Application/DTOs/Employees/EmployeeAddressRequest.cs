using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Employees;

public class EmployeeAddressRequest
{
    public AddressType AddressType { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? District { get; set; }
    public string? City { get; set; }
    public string? ZipCode { get; set; }
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? HouseNumber { get; set; }
}
