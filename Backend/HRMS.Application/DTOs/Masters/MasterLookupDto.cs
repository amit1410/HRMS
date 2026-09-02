namespace HRMS.Application.DTOs.Masters;

/// <summary>
/// Lightweight read DTO for all master data dropdowns. Every master table returns this
/// shape so the frontend only needs one component for all dropdowns.
/// Displays as "{Code} - {Name}" in the UI.
/// </summary>
public record MasterLookupDto(Guid Id, string Code, string Name, bool IsActive);
