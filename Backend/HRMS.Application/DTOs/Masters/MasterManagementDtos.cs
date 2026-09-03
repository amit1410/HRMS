using HRMS.Application.Common;

namespace HRMS.Application.DTOs.Masters;

public sealed class MasterManagementRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? ParentId { get; set; }
}

public sealed record MasterManagementRecordDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsActive,
    Guid? ParentId,
    string? ParentCode,
    string? ParentName);

public sealed record MasterManagementQuery(
    string? Search = null,
    bool? IsActive = null,
    Guid? ParentId = null,
    int Page = 1,
    int PageSize = 50);

public sealed record MasterManagementPage(
    IReadOnlyList<MasterManagementRecordDto> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
