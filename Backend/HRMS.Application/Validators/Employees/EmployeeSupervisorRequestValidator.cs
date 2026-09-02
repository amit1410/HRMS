using FluentValidation;
using HRMS.Application.DTOs.Employees;

namespace HRMS.Application.Validators.Employees;

/// <summary>
/// Shape and cross-field validation for employee supervisor assignment. All properties are optional —
/// any required presence or referential-integrity checks are handled by the service layer.
/// </summary>
public class EmployeeSupervisorRequestValidator : AbstractValidator<EmployeeSupervisorRequest>
{
    public EmployeeSupervisorRequestValidator()
    {
    }
}
