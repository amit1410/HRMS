using System;

namespace HRMS.Domain.Enums;

/// <summary>
/// Flags enum for supervisor/manager type categories. Each value represents a role
/// an employee can be eligible for in the supervisor hierarchy. Used with bitwise
/// operations to determine eligibility.
/// </summary>
[Flags]
public enum SupervisorType
{
    None = 0,
    L1 = 1,
    L2 = 2,
    L3 = 4,
    Other = 8,
    HR = 16,
    Time = 32
}
