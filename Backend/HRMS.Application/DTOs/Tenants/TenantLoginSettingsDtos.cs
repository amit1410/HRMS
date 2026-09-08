using HRMS.Domain.Enums;

namespace HRMS.Application.DTOs.Tenants;

public sealed record TenantLoginSettingsDto(TenantLoginIdentifierMode LoginIdentifierMode);

public sealed record UpdateTenantLoginSettingsRequest(TenantLoginIdentifierMode LoginIdentifierMode);
