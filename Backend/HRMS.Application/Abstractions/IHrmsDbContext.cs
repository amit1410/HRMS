using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HRMS.Application.Abstractions;

/// <summary>
/// The persistence surface the Application layer is allowed to use. Application services depend on
/// this abstraction rather than on the concrete <c>HrmsDbContext</c>, which keeps business logic in the
/// Application layer while the dependency direction still points inward (Infrastructure implements it).
/// EF Core's DbSet is exposed directly and intentionally: per-entity repositories would add a layer
/// without adding capability, and LINQ against a DbSet is already a testable, provider-agnostic API.
/// <para>
/// This reaches <em>one</em> tenant's database — the request's own shard — and every tenant-scoped set on
/// it is filtered to that tenant besides. Anything that has to run before a tenant is known belongs on
/// <see cref="IHrmsCatalogDbContext"/> instead.
/// </para>
/// </summary>
public interface IHrmsDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<AccountEmployeeCurrentLink> AccountEmployeeCurrentLinks { get; }
    DbSet<AccountEmployeeLinkEvent> AccountEmployeeLinkEvents { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Department> Departments { get; }
    DbSet<Designation> Designations { get; }
    DbSet<Bank> Banks { get; }
    DbSet<Country> Countries { get; }
    DbSet<State> States { get; }
    DbSet<City> Cities { get; }
    DbSet<Employee> Employees { get; }

    // Organizational hierarchy masters
    DbSet<HoldingCompany> HoldingCompanies { get; }
    DbSet<Lob> LinesOfBusiness { get; }
    DbSet<Organisation> Organisations { get; }
    DbSet<SubDepartment> SubDepartments { get; }
    DbSet<Section> Sections { get; }
    DbSet<SubSection> SubSections { get; }
    DbSet<Function> Functions { get; }
    DbSet<SubFunction> SubFunctions { get; }
    DbSet<Grade> Grades { get; }
    DbSet<EmployeeType> EmployeeTypes { get; }
    DbSet<WorkLocation> WorkLocations { get; }
    DbSet<CostCenter> CostCenters { get; }
    DbSet<PositionChangeReason> PositionChangeReasons { get; }
    DbSet<EmployeeCodeConfig> EmployeeCodeConfigs { get; }
    DbSet<EmployeeCodeRule> EmployeeCodeRules { get; }
    DbSet<EmployeeCodeConfigVersion> EmployeeCodeConfigVersions { get; }
    DbSet<EmployeeCodeRuleCondition> EmployeeCodeRuleConditions { get; }
    DbSet<EmployeeCodeSegment> EmployeeCodeSegments { get; }
    DbSet<EmployeeCodeSequence> EmployeeCodeSequences { get; }

    // Employee sub-entities
    DbSet<EmployeeContact> EmployeeContacts { get; }
    DbSet<EmployeeAddress> EmployeeAddresses { get; }
    DbSet<EmployeeFamily> EmployeeFamilyMembers { get; }
    DbSet<EmployeeEducation> EmployeeEducationRecords { get; }
    DbSet<EmployeeEmploymentHistory> EmployeeEmploymentHistory { get; }
    DbSet<EmployeePreviousEmployment> EmployeePreviousEmployments { get; }
    DbSet<EmployeeBankDetail> EmployeeBankDetails { get; }
    DbSet<EmployeeDocument> EmployeeDocuments { get; }
    DbSet<EmployeeSupervisor> EmployeeSupervisors { get; }
    DbSet<EmployeeAdditionalInfo> EmployeeAdditionalInfo { get; }
    DbSet<EmployeeAuditLog> EmployeeAuditLogs { get; }
    DbSet<EmployeeEmployment> EmployeeEmployments { get; }
    DbSet<ImportBatch> ImportBatches { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
