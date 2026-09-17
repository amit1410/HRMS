using HRMS.Application.Common;
using HRMS.Application.DTOs.Authorization;

namespace HRMS.Application.Abstractions;

public interface IPageAccessService
{
    Task<Result<IReadOnlyList<PageAccessRoleDto>>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<Result<PageAccessMatrixDto>> GetMatrixAsync(int roleId, CancellationToken cancellationToken = default);
    Task<Result<PageAccessMatrixDto>> UpdateAsync(int roleId, PageAccessUpdateRequest request, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<NavigationItemDto>>> GetNavigationAsync(CancellationToken cancellationToken = default);
    Task<Result<UserAccessPreviewDto>> GetUserPreviewAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Result<IReadOnlyList<AccessPreviewUserDto>>> SearchUsersAsync(string? search, CancellationToken cancellationToken = default);
    Task<Result<PagedResult<PageAccessHistoryItemDto>>> GetHistoryAsync(PageAccessHistoryQuery query, CancellationToken cancellationToken = default);
}
