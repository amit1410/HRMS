using HRMS.Domain.Authorization;
using HRMS.Domain.Entities;
using HRMS.Domain.Enums;
using DomainPermissions = HRMS.Domain.Authorization.Permissions;

namespace HRMS.Infrastructure.Persistence.Seed;

/// <summary>A user to be seeded, plus the role it should hold. Password is applied by the seeder.</summary>
public record SeedUser(Guid Id, Guid TenantId, string Email, string FirstName, string LastName, string RoleName);

/// <summary>An organizational unit to be seeded.</summary>
public record SeedDepartment(Guid Id, Guid TenantId, string Code, string Name, string Description);

/// <summary>A job title to be seeded.</summary>
public record SeedDesignation(Guid Id, Guid TenantId, string Code, string Name, string Description);

/// <summary>A bank master row to be seeded for a tenant.</summary>
public record SeedBank(Guid Id, Guid TenantId, string Code, string Name, string Description, bool IsActive);

/// <summary>An organizational hierarchy master row (holding company, line of business, etc.) to be seeded.</summary>
public record SeedHoldingCompany(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive);

public record SeedLob(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, Guid? HoldingCompanyId);

public record SeedOrganisation(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive);

public record SeedSubDepartment(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, Guid DepartmentId);

public record SeedSection(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, Guid? SubDepartmentId);

public record SeedSubSection(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, Guid? SectionId);

public record SeedFunction(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive);

public record SeedSubFunction(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, Guid? FunctionId);

public record SeedGrade(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, int SortOrder);

public record SeedWorkLocation(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive);

public record SeedEmployeeType(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive, int SortOrder);

public record SeedCostCenter(Guid Id, Guid TenantId, string Code, string Name, string? Description, bool IsActive);

/// <summary>
/// An employee to be seeded. Department, designation and manager are given as <em>codes</em> rather than
/// ids: the seed list stays readable, and the seeder resolves each code within the same tenant, so a
/// cross-tenant reference cannot be written by a typo in this file.
/// </summary>
public record SeedEmployee(
    Guid Id,
    Guid TenantId,
    string EmployeeCode,
    string FirstName,
    string LastName,
    string Email,
    string Phone,
    DateOnly DateOfBirth,
    Gender Gender,
    DateOnly DateOfJoining,
    string DepartmentCode,
    string DesignationCode,
    string? ReportingManagerCode,
    string Address);

/// <summary>
/// Canonical development seed graph: roles, permissions, role→permission grants, two demo tenants,
/// and sample users for each. All ids are fixed so seeding is deterministic and idempotent, which is
/// what lets us reliably demonstrate that tenant A cannot see tenant B's data.
/// </summary>
public static class SeedData
{
    /// <summary>Shared development password for every seeded user. Development-only — never ship real credentials.</summary>
    public const string DefaultUserPassword = "Passw0rd!";

    public static class TenantIds
    {
        public static readonly Guid Demo01 = new("11111111-1111-1111-1111-111111111111");
        public static readonly Guid Demo02 = new("22222222-2222-2222-2222-222222222222");
    }

    /// <summary>
    /// Role ids, written out rather than computed from a position in <see cref="RoleNames.All"/>.
    /// <para>
    /// These are <c>ValueGeneratedNever</c> columns, and the same reference rows are seeded into every
    /// tenant database — so an id here is a value shared across databases that may have been seeded by
    /// different builds. Deriving it from a list position meant that reordering that list, a change with no
    /// other consequence anywhere, renumbered every row after the one that moved, while
    /// <see cref="RolePermissionMap"/> went on writing grants under the new numbering. A tenant database
    /// seeded before the reorder would then hold one role's id against another role's permissions, and
    /// nothing in the system would report it: the ids all still resolve.
    /// </para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, int> RoleIds =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RoleNames.SuperAdmin] = 1,
            [RoleNames.TenantAdmin] = 2,
            [RoleNames.HRAdmin] = 3,
            [RoleNames.HRManager] = 4,
            [RoleNames.Manager] = 5,
            [RoleNames.Employee] = 6,
            [RoleNames.AccountLinkAdministrator] = 7,
            [RoleNames.AccountLinkAuditor] = 8,
            [RoleNames.EmployeeRelationshipOfficer] = 9,
            [RoleNames.HRBP] = 10,
            [RoleNames.TimeManager] = 11,
            [RoleNames.IT] = 12,
            [RoleNames.Accounts] = 13,
            [RoleNames.SuperHR] = 14
        };

    /// <summary>Permission ids. Fixed for the same reasons as <see cref="RoleIds"/>.</summary>
    private static readonly IReadOnlyDictionary<string, int> PermissionIds =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [DomainPermissions.Employee.View] = 1,
            [DomainPermissions.Employee.Create] = 2,
            [DomainPermissions.Employee.Edit] = 3,
            [DomainPermissions.Employee.Delete] = 4,
            [DomainPermissions.Employee.Export] = 5,
            [DomainPermissions.Department.View] = 6,
            [DomainPermissions.Department.Create] = 7,
            [DomainPermissions.Department.Edit] = 8,
            [DomainPermissions.Department.Delete] = 9,
            [DomainPermissions.Designation.View] = 10,
            [DomainPermissions.Designation.Create] = 11,
            [DomainPermissions.Designation.Edit] = 12,
            [DomainPermissions.Designation.Delete] = 13,
            [DomainPermissions.User.View] = 14,
            [DomainPermissions.User.Create] = 15,
            [DomainPermissions.User.Edit] = 16,
            [DomainPermissions.User.Delete] = 17,
            [DomainPermissions.Employee.Import] = 18,
            [DomainPermissions.EmploymentHistory.View] = 19,
            [DomainPermissions.EmploymentHistory.Change] = 20,
            [DomainPermissions.EmployeeSensitive.View] = 21,
            [DomainPermissions.EmployeeSensitive.Edit] = 22,
            [DomainPermissions.Geography.View] = 23,
            [DomainPermissions.Geography.Manage] = 24,
            [DomainPermissions.EmployeeCodeConfiguration.View] = 25,
            [DomainPermissions.EmployeeCodeConfiguration.Manage] = 26,
            [DomainPermissions.AccountEmployeeLink.View] = 27,
            [DomainPermissions.AccountEmployeeLink.ViewHistory] = 28,
            [DomainPermissions.AccountEmployeeLink.Manage] = 29,
            [DomainPermissions.RoleManagement.View] = 68,
            [DomainPermissions.RoleManagement.Manage] = 69,
            [DomainPermissions.RoleManagement.AssignmentView] = 70,
            [DomainPermissions.RoleManagement.AssignmentManage] = 71,
            [DomainPermissions.RoleManagement.AssignmentViewHistory] = 72,
        [DomainPermissions.PageAccess.View] = 73,
        [DomainPermissions.PageAccess.Manage] = 74,
            [DomainPermissions.Payroll.SalaryComponentView] = 75,
            [DomainPermissions.Payroll.SalaryComponentManage] = 76,
            [DomainPermissions.Payroll.SalaryComponentViewHistory] = 77,
            [DomainPermissions.Payroll.SalaryStructureView] = 78,
            [DomainPermissions.Payroll.SalaryStructureManage] = 79,
            [DomainPermissions.Payroll.SalaryStructureViewHistory] = 80,
            [DomainPermissions.Payroll.EmployeeSalaryView] = 81,
            [DomainPermissions.Payroll.EmployeeSalaryManage] = 82,
            [DomainPermissions.Payroll.EmployeeSalaryViewHistory] = 83,
            [DomainPermissions.Payroll.PeriodView] = 84,
            [DomainPermissions.Payroll.PeriodManage] = 85,
            [DomainPermissions.Payroll.PeriodUnlock] = 100,
            [DomainPermissions.Payroll.ControlsView] = 101,
            [DomainPermissions.Payroll.ControlsManage] = 102,
            [DomainPermissions.Payroll.PayslipViewAll] = 103,
            [DomainPermissions.Payroll.PayslipGenerate] = 104,
            [DomainPermissions.Payroll.PayslipPublish] = 105,
            [DomainPermissions.Payroll.PayslipViewHistory] = 106,
            [DomainPermissions.Payroll.RegisterView] = 107,
            [DomainPermissions.Payroll.RegisterExport] = 108,
            [DomainPermissions.Payroll.PayslipViewOwn] = 109,
            [DomainPermissions.Payroll.BankAdviceView] = 110,
            [DomainPermissions.Payroll.BankAdviceGenerate] = 111,
            [DomainPermissions.Payroll.BankAdviceValidate] = 112,
            [DomainPermissions.Payroll.BankAdviceApprove] = 113,
            [DomainPermissions.Payroll.BankAdviceExport] = 114,
            [DomainPermissions.Payroll.BankAdviceCancel] = 115,
            [DomainPermissions.Payroll.BankAdviceViewHistory] = 116,
            [DomainPermissions.Payroll.RetroView] = 117,
            [DomainPermissions.Payroll.RetroEvaluate] = 118,
            [DomainPermissions.Payroll.RetroApprove] = 119,
            [DomainPermissions.Payroll.RetroApply] = 120,
            [DomainPermissions.Payroll.RetroCancel] = 121,
            [DomainPermissions.Payroll.FinalSettlementView] = 122,
            [DomainPermissions.Payroll.FinalSettlementManage] = 123,
            [DomainPermissions.Payroll.FinalSettlementCalculate] = 124,
            [DomainPermissions.Payroll.FinalSettlementApprove] = 125,
            [DomainPermissions.Payroll.FinalSettlementFinalize] = 126,
            [DomainPermissions.Payroll.FinalSettlementCancel] = 127,
            [DomainPermissions.Payroll.AccountingView] = 128,
            [DomainPermissions.Payroll.AccountingGenerate] = 129,
            [DomainPermissions.Payroll.AccountingValidate] = 130,
            [DomainPermissions.Payroll.AccountingApprove] = 131,
            [DomainPermissions.Payroll.AccountingPost] = 132,
            [DomainPermissions.Payroll.AccountingExport] = 133,
            [DomainPermissions.Payroll.AccountingViewHistory] = 134,
            [DomainPermissions.Payroll.AccountingManageConfiguration] = 135,
            [DomainPermissions.Payroll.StatutoryComplianceView] = 136,
            [DomainPermissions.Payroll.StatutoryComplianceGenerate] = 137,
            [DomainPermissions.Payroll.StatutoryComplianceValidate] = 138,
            [DomainPermissions.Payroll.StatutoryComplianceApprove] = 139,
            [DomainPermissions.Payroll.StatutoryComplianceExport] = 140,
            [DomainPermissions.Payroll.StatutoryComplianceMarkFiled] = 141,
            [DomainPermissions.Payroll.StatutoryComplianceCancel] = 142,
            [DomainPermissions.Payroll.StatutoryComplianceViewHistory] = 143,
            [DomainPermissions.Payroll.StatutoryComplianceManagePeriods] = 144,
            [DomainPermissions.Payroll.LoansView] = 145,
            [DomainPermissions.Payroll.LoansRequest] = 146,
            [DomainPermissions.Payroll.LoansManage] = 147,
            [DomainPermissions.Payroll.LoansApprove] = 148,
            [DomainPermissions.Payroll.LoansDisburse] = 149,
            [DomainPermissions.Payroll.LoansRecover] = 150,
            [DomainPermissions.Payroll.LoansClose] = 151,
            [DomainPermissions.Payroll.LoansCancel] = 152,
            [DomainPermissions.Payroll.LoansViewHistory] = 153,
            [DomainPermissions.Payroll.LoansManageProducts] = 154,
            [DomainPermissions.Payroll.ReimbursementsView] = 155,
            [DomainPermissions.Payroll.ReimbursementsRequest] = 156,
            [DomainPermissions.Payroll.ReimbursementsManage] = 157,
            [DomainPermissions.Payroll.ReimbursementsApprove] = 158,
            [DomainPermissions.Payroll.ReimbursementsSettle] = 159,
            [DomainPermissions.Payroll.ReimbursementsCancel] = 160,
            [DomainPermissions.Payroll.ReimbursementsViewHistory] = 161,
            [DomainPermissions.Payroll.ReimbursementsManageCategories] = 162,
            [DomainPermissions.Payroll.SeparationBenefitsView] = 163,
            [DomainPermissions.Payroll.SeparationBenefitsCalculate] = 164,
            [DomainPermissions.Payroll.SeparationBenefitsManagePolicies] = 165,
            [DomainPermissions.Payroll.SeparationBenefitsOverride] = 166,
            [DomainPermissions.Payroll.SeparationBenefitsApprove] = 167,
            [DomainPermissions.Payroll.SeparationBenefitsViewHistory] = 168,
            [DomainPermissions.Payroll.VariablePayView] = 169,
            [DomainPermissions.Payroll.VariablePayManagePlans] = 170,
            [DomainPermissions.Payroll.VariablePayCalculate] = 171,
            [DomainPermissions.Payroll.VariablePayCreateAward] = 172,
            [DomainPermissions.Payroll.VariablePaySubmit] = 173,
            [DomainPermissions.Payroll.VariablePayApprove] = 174,
            [DomainPermissions.Payroll.VariablePayOverride] = 175,
            [DomainPermissions.Payroll.VariablePayCancel] = 176,
            [DomainPermissions.Payroll.VariablePayViewHistory] = 177,
            [DomainPermissions.Payroll.AdjustmentsView] = 178,
            [DomainPermissions.Payroll.AdjustmentsCreate] = 179,
            [DomainPermissions.Payroll.AdjustmentsSubmit] = 180,
            [DomainPermissions.Payroll.AdjustmentsApprove] = 181,
            [DomainPermissions.Payroll.AdjustmentsCancel] = 182,
            [DomainPermissions.Payroll.AdjustmentsReverse] = 183,
            [DomainPermissions.Payroll.AdjustmentsViewHistory] = 184,
            [DomainPermissions.Payroll.OffCycleView] = 185,
            [DomainPermissions.Payroll.OffCycleCreate] = 186,
            [DomainPermissions.Payroll.OffCycleApprove] = 187,
            [DomainPermissions.Payroll.OffCycleProcess] = 188,
            [DomainPermissions.Payroll.OffCycleCancel] = 189,
            [DomainPermissions.Payroll.AnalyticsView] = 190,
            [DomainPermissions.Payroll.AnalyticsConfigure] = 191,
            [DomainPermissions.Payroll.ReconciliationView] = 192,
            [DomainPermissions.Payroll.ReconciliationGenerate] = 193,
            [DomainPermissions.Payroll.ReconciliationAcknowledge] = 194,
            [DomainPermissions.Payroll.ReconciliationResolve] = 195,
            [DomainPermissions.Payroll.ExceptionsView] = 196,
            [DomainPermissions.Payroll.ExceptionsManage] = 197,
            [DomainPermissions.Payroll.TaxDeclarationViewOwn] = 198,
            [DomainPermissions.Payroll.TaxDeclarationManageOwn] = 199,
            [DomainPermissions.Payroll.TaxDeclarationReview] = 200,
            [DomainPermissions.Payroll.TaxDeclarationApprove] = 201,
            [DomainPermissions.Payroll.TaxDeclarationReopen] = 202,
            [DomainPermissions.Payroll.TaxDeclarationConfigure] = 203,
            [DomainPermissions.Payroll.TaxDeclarationViewAudit] = 204,
            [DomainPermissions.Payroll.YearEndTaxView] = 215,
            [DomainPermissions.Payroll.YearEndTaxManage] = 216,
            [DomainPermissions.Payroll.YearEndTaxCalculate] = 217,
            [DomainPermissions.Payroll.YearEndTaxSubmit] = 218,
            [DomainPermissions.Payroll.YearEndTaxApprove] = 219,
            [DomainPermissions.Payroll.YearEndTaxClose] = 220,
            [DomainPermissions.Payroll.YearEndTaxViewHistory] = 221,
            [DomainPermissions.Payroll.YearEndTaxExport] = 222,
            [DomainPermissions.Payroll.InputView] = 205,
            [DomainPermissions.Payroll.InputCreate] = 206,
            [DomainPermissions.Payroll.InputImport] = 207,
            [DomainPermissions.Payroll.InputValidate] = 208,
            [DomainPermissions.Payroll.InputSubmit] = 209,
            [DomainPermissions.Payroll.InputApprove] = 210,
            [DomainPermissions.Payroll.InputPost] = 211,
            [DomainPermissions.Payroll.InputCancel] = 212,
            [DomainPermissions.Payroll.InputViewAudit] = 213,
            [DomainPermissions.Payroll.InputConfigureTemplate] = 214,
            [DomainPermissions.Payroll.StatutoryFilingView] = 223,
            [DomainPermissions.Payroll.StatutoryFilingManage] = 224,
            [DomainPermissions.Payroll.StatutoryFilingGenerate] = 225,
            [DomainPermissions.Payroll.StatutoryFilingValidate] = 226,
            [DomainPermissions.Payroll.StatutoryFilingSubmitForApproval] = 227,
            [DomainPermissions.Payroll.StatutoryFilingApprove] = 228,
            [DomainPermissions.Payroll.StatutoryFilingSubmit] = 229,
            [DomainPermissions.Payroll.StatutoryFilingCancel] = 230,
            [DomainPermissions.Payroll.StatutoryFilingExport] = 231,
            [DomainPermissions.Payroll.StatutoryFilingViewHistory] = 232,
            [DomainPermissions.Payroll.StatutoryFilingManageConnections] = 233,
            [DomainPermissions.Payroll.RunView] = 86,
            [DomainPermissions.Payroll.RunManage] = 87,
            [DomainPermissions.Payroll.RunPrepare] = 88,
            [DomainPermissions.Payroll.RunApprove] = 89,
            [DomainPermissions.Payroll.RunFinalize] = 90,
            [DomainPermissions.Payroll.RunViewHistory] = 91,
            [DomainPermissions.Payroll.RunCalculate] = 92,
            [DomainPermissions.Payroll.RunRecalculate] = 93,
            [DomainPermissions.Payroll.RunViewResults] = 94,
            [DomainPermissions.Payroll.StatutoryView] = 95,
            [DomainPermissions.Payroll.StatutoryManage] = 96,
            [DomainPermissions.Payroll.StatutoryViewHistory] = 97,
            [DomainPermissions.Payroll.EmployeeStatutoryView] = 98,
            [DomainPermissions.Payroll.EmployeeStatutoryManage] = 99,
            [DomainPermissions.Leave.TypeManage] = 30,
            [DomainPermissions.Leave.PeriodManage] = 31,
            [DomainPermissions.Leave.PolicyView] = 32,
            [DomainPermissions.Leave.PolicyManage] = 33,
            [DomainPermissions.Leave.PolicyPublish] = 34,
            [DomainPermissions.Leave.Approve] = 35,
            [DomainPermissions.Leave.BalanceView] = 36,
            [DomainPermissions.Leave.BalanceAdjust] = 37,
            [DomainPermissions.Leave.BalanceImport] = 38,
            [DomainPermissions.Leave.BalanceViewImportHistory] = 39,
            [DomainPermissions.Leave.DashboardViewAll] = 40,
            [DomainPermissions.Leave.ReportsView] = 41,
            [DomainPermissions.Leave.ReportsExport] = 42,
            [DomainPermissions.Attendance.View] = 43,
            [DomainPermissions.Attendance.ShiftManage] = 44,
            [DomainPermissions.Attendance.PatternManage] = 45,
        [DomainPermissions.Attendance.RosterManage] = 46,
        [DomainPermissions.Attendance.RosterUpload] = 47,
        [DomainPermissions.Attendance.RegularizationRequest] = 48,
        [DomainPermissions.Attendance.RegularizationApprove] = 49,
        [DomainPermissions.Attendance.OnDutyRequest] = 50,
        [DomainPermissions.Attendance.OnDutyApprove] = 51,
        [DomainPermissions.Attendance.MonthlyViewSelf] = 52,
        [DomainPermissions.Attendance.MonthlyViewTeam] = 53,
        [DomainPermissions.Attendance.MonthlyViewAll] = 54,
        [DomainPermissions.Attendance.MonthlyProcess] = 55,
        [DomainPermissions.Attendance.ExceptionView] = 56,
        [DomainPermissions.Attendance.MonthlyClose] = 57,
        [DomainPermissions.Attendance.MonthlyReopen] = 58,
        [DomainPermissions.Attendance.AdminCorrectionManage] = 59,
            [DomainPermissions.Attendance.ReportView] = 60,
            [DomainPermissions.Attendance.ReportExport] = 61,
            [DomainPermissions.Separation.ViewSelf] = 234,
            [DomainPermissions.Separation.ViewTeam] = 235,
            [DomainPermissions.Separation.ViewAll] = 236,
            [DomainPermissions.Separation.CreateSelf] = 237,
            [DomainPermissions.Separation.Initiate] = 238,
            [DomainPermissions.Separation.Submit] = 239,
            [DomainPermissions.Separation.Withdraw] = 240,
            [DomainPermissions.Separation.Review] = 241,
            [DomainPermissions.Separation.Manage] = 242,
            [DomainPermissions.Separation.ViewHistory] = 243,
            [DomainPermissions.Separation.ManagerReview] = 244,
            [DomainPermissions.Separation.HrReview] = 245,
            [DomainPermissions.Separation.ReviseLastWorkingDate] = 246,
            [DomainPermissions.Separation.Approve] = 247,
            [DomainPermissions.Separation.Reject] = 248,
            [DomainPermissions.Leave.RequestCreate] = 62,
            [DomainPermissions.Leave.RequestViewOwn] = 63,
            [DomainPermissions.Leave.RequestWithdrawOwn] = 64,
            [DomainPermissions.Leave.RequestCancelOwn] = 65,
            [DomainPermissions.Leave.BalanceViewOwn] = 66,
            [DomainPermissions.Leave.TypeViewAvailable] = 67,
        };

    /// <summary>The fixed id for a role. Throws for a role that has not been given one.</summary>
    public static int RoleId(string name) =>
        RoleIds.TryGetValue(name, out var id)
            ? id
            : throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Role '{name}' has no seeded id. Add it to {nameof(SeedData)}.{nameof(RoleIds)} with the "
                + "next unused number; never reuse or renumber an existing one.");

    /// <summary>The fixed id for a permission. Throws for a permission that has not been given one.</summary>
    public static int PermissionId(string name) =>
        PermissionIds.TryGetValue(name, out var id)
            ? id
            : throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Permission '{name}' has no seeded id. Add it to {nameof(SeedData)}.{nameof(PermissionIds)} "
                + "with the next unused number; never reuse or renumber an existing one.");

    private static readonly IReadOnlyDictionary<string, string> RoleDescriptions = new Dictionary<string, string>
    {
        [RoleNames.SuperAdmin] = "Tenant super administrator with full access within its own tenant.",
        [RoleNames.TenantAdmin] = "Administrator with full access within their own tenant.",
        [RoleNames.HRAdmin] = "HR administrator managing employees, departments and designations.",
        [RoleNames.HRManager] = "HR manager with employee management capabilities.",
        [RoleNames.Manager] = "Manager with read access to organizational data.",
        [RoleNames.Employee] = "Standard employee with self-service access.",
        [RoleNames.AccountLinkAdministrator] = "Named operator who can manage account-to-employee links.",
        [RoleNames.AccountLinkAuditor] = "Named operator who can review account-to-employee link history."
        ,[RoleNames.EmployeeRelationshipOfficer] = "Employee relationship officer."
        ,[RoleNames.HRBP] = "Human resources business partner."
        ,[RoleNames.TimeManager] = "Time and attendance manager."
        ,[RoleNames.IT] = "Information technology operator."
        ,[RoleNames.Accounts] = "Accounts operator."
        ,[RoleNames.SuperHR] = "Privileged tenant HR administrator."
    };

    public static IReadOnlyList<Role> Roles =>
        RoleNames.All.Select(name => new Role
        {
            Id = RoleId(name),
            Name = name,
            Description = RoleDescriptions[name]
        }).ToList();

    public static IReadOnlyList<Permission> Permissions =>
        DomainPermissions.All.Select(name => new Permission
        {
            Id = PermissionId(name),
            Name = name,
            Description = name.Replace('.', ' ')
        }).ToList();

    /// <summary>Maps each role to the permission names it is granted.</summary>
    public static IReadOnlyDictionary<string, string[]> RolePermissionMap => new Dictionary<string, string[]>
    {
        [RoleNames.SuperAdmin] = DomainPermissions.All.Where(x => !x.StartsWith("AccountEmployeeLink.", StringComparison.Ordinal)).ToArray(),
        [RoleNames.TenantAdmin] = DomainPermissions.All
            .Where(x => !x.StartsWith("AccountEmployeeLink.", StringComparison.Ordinal))
            .ToArray(),
        [RoleNames.HRAdmin] = new[]
        {
            DomainPermissions.Employee.View, DomainPermissions.Employee.Create, DomainPermissions.Employee.Edit,
            DomainPermissions.Employee.Delete, DomainPermissions.Employee.Export, DomainPermissions.Employee.Import,
            DomainPermissions.EmployeeSensitive.View, DomainPermissions.EmployeeSensitive.Edit,
            DomainPermissions.Geography.View, DomainPermissions.Geography.Manage,
            DomainPermissions.EmploymentHistory.View, DomainPermissions.EmploymentHistory.Change,
            DomainPermissions.Department.View, DomainPermissions.Department.Create, DomainPermissions.Department.Edit,
            DomainPermissions.Department.Delete,
            DomainPermissions.Designation.View, DomainPermissions.Designation.Create, DomainPermissions.Designation.Edit,
            DomainPermissions.Designation.Delete,
            DomainPermissions.Leave.DashboardViewAll,
            DomainPermissions.Leave.ReportsView, DomainPermissions.Leave.ReportsExport,
            DomainPermissions.User.View, DomainPermissions.User.Create, DomainPermissions.User.Edit
            , DomainPermissions.Attendance.View, DomainPermissions.Attendance.ShiftManage,
            DomainPermissions.Attendance.PatternManage, DomainPermissions.Attendance.RosterManage,
            DomainPermissions.Attendance.RosterUpload, DomainPermissions.Attendance.MonthlyViewAll,
            DomainPermissions.Attendance.MonthlyProcess, DomainPermissions.Attendance.MonthlyClose,
            DomainPermissions.Attendance.MonthlyReopen, DomainPermissions.Attendance.ExceptionView
            , DomainPermissions.Attendance.ReportView, DomainPermissions.Attendance.ReportExport
            , DomainPermissions.PageAccess.View, DomainPermissions.PageAccess.Manage
            , DomainPermissions.Payroll.SalaryComponentView, DomainPermissions.Payroll.SalaryComponentManage, DomainPermissions.Payroll.SalaryComponentViewHistory
            , DomainPermissions.Payroll.SalaryStructureView, DomainPermissions.Payroll.SalaryStructureManage, DomainPermissions.Payroll.SalaryStructureViewHistory
            , DomainPermissions.Payroll.EmployeeSalaryView, DomainPermissions.Payroll.EmployeeSalaryManage, DomainPermissions.Payroll.EmployeeSalaryViewHistory
            , DomainPermissions.Payroll.PeriodView, DomainPermissions.Payroll.PeriodManage, DomainPermissions.Payroll.PeriodUnlock, DomainPermissions.Payroll.ControlsView, DomainPermissions.Payroll.ControlsManage, DomainPermissions.Payroll.RunView, DomainPermissions.Payroll.RunManage, DomainPermissions.Payroll.RunPrepare, DomainPermissions.Payroll.RunApprove, DomainPermissions.Payroll.RunFinalize, DomainPermissions.Payroll.RunViewHistory, DomainPermissions.Payroll.RunCalculate, DomainPermissions.Payroll.RunRecalculate, DomainPermissions.Payroll.RunViewResults
            , DomainPermissions.Payroll.TaxDeclarationViewOwn, DomainPermissions.Payroll.TaxDeclarationManageOwn, DomainPermissions.Payroll.TaxDeclarationReview, DomainPermissions.Payroll.TaxDeclarationApprove, DomainPermissions.Payroll.TaxDeclarationReopen, DomainPermissions.Payroll.TaxDeclarationConfigure, DomainPermissions.Payroll.TaxDeclarationViewAudit, DomainPermissions.Payroll.YearEndTaxView, DomainPermissions.Payroll.YearEndTaxManage, DomainPermissions.Payroll.YearEndTaxCalculate, DomainPermissions.Payroll.YearEndTaxSubmit, DomainPermissions.Payroll.YearEndTaxApprove, DomainPermissions.Payroll.YearEndTaxClose, DomainPermissions.Payroll.YearEndTaxViewHistory, DomainPermissions.Payroll.YearEndTaxExport
            , DomainPermissions.Separation.ViewSelf, DomainPermissions.Separation.ViewTeam, DomainPermissions.Separation.ViewAll, DomainPermissions.Separation.CreateSelf, DomainPermissions.Separation.Initiate, DomainPermissions.Separation.Submit, DomainPermissions.Separation.Withdraw, DomainPermissions.Separation.Review, DomainPermissions.Separation.Manage, DomainPermissions.Separation.ViewHistory, DomainPermissions.Separation.ManagerReview, DomainPermissions.Separation.HrReview, DomainPermissions.Separation.ReviseLastWorkingDate, DomainPermissions.Separation.Approve, DomainPermissions.Separation.Reject
        },
        [RoleNames.HRManager] = new[]
        {
            DomainPermissions.Employee.View, DomainPermissions.Employee.Create, DomainPermissions.Employee.Edit,
            DomainPermissions.Employee.Export,
            DomainPermissions.EmployeeSensitive.View, DomainPermissions.EmployeeSensitive.Edit,
            DomainPermissions.Geography.View,
            DomainPermissions.EmploymentHistory.View, DomainPermissions.EmploymentHistory.Change,
            DomainPermissions.Department.View, DomainPermissions.Designation.View,
            DomainPermissions.Attendance.ExceptionView, DomainPermissions.Attendance.ReportView,
            DomainPermissions.Attendance.ReportExport,
            DomainPermissions.Payroll.TaxDeclarationReview, DomainPermissions.Payroll.TaxDeclarationViewAudit, DomainPermissions.Payroll.YearEndTaxView, DomainPermissions.Payroll.YearEndTaxViewHistory,
            DomainPermissions.Separation.ViewSelf, DomainPermissions.Separation.ViewTeam, DomainPermissions.Separation.Initiate, DomainPermissions.Separation.Submit, DomainPermissions.Separation.Review, DomainPermissions.Separation.ViewHistory, DomainPermissions.Separation.ManagerReview, DomainPermissions.Separation.HrReview, DomainPermissions.Separation.ReviseLastWorkingDate, DomainPermissions.Separation.Approve, DomainPermissions.Separation.Reject
        },
        [RoleNames.Manager] = new[]
        {
            DomainPermissions.Employee.View, DomainPermissions.Department.View, DomainPermissions.Designation.View,
            DomainPermissions.Geography.View, DomainPermissions.Attendance.MonthlyViewTeam,
            DomainPermissions.Leave.Approve,
            DomainPermissions.Separation.ViewSelf, DomainPermissions.Separation.ViewTeam, DomainPermissions.Separation.Review, DomainPermissions.Separation.ViewHistory, DomainPermissions.Separation.ManagerReview, DomainPermissions.Separation.Reject
        },
        [RoleNames.Employee] = new[]
        {
            DomainPermissions.Geography.View,
            DomainPermissions.Attendance.MonthlyViewSelf,
            DomainPermissions.Leave.RequestCreate,
            DomainPermissions.Leave.RequestViewOwn,
            DomainPermissions.Leave.RequestWithdrawOwn,
            DomainPermissions.Leave.RequestCancelOwn,
            DomainPermissions.Leave.BalanceViewOwn,
            DomainPermissions.Leave.TypeViewAvailable,
            DomainPermissions.Payroll.AdjustmentsView,
            DomainPermissions.Payroll.AdjustmentsViewHistory,
            DomainPermissions.Payroll.TaxDeclarationViewOwn,
            DomainPermissions.Payroll.TaxDeclarationManageOwn,
            DomainPermissions.Separation.ViewSelf, DomainPermissions.Separation.CreateSelf, DomainPermissions.Separation.Submit, DomainPermissions.Separation.Withdraw, DomainPermissions.Separation.ViewHistory
        },
        [RoleNames.AccountLinkAdministrator] = new[] { DomainPermissions.AccountEmployeeLink.View, DomainPermissions.AccountEmployeeLink.Manage },
        [RoleNames.AccountLinkAuditor] = new[] { DomainPermissions.AccountEmployeeLink.View, DomainPermissions.AccountEmployeeLink.ViewHistory }
        ,[RoleNames.EmployeeRelationshipOfficer] = new[] { DomainPermissions.Employee.View, DomainPermissions.EmploymentHistory.View }
        ,[RoleNames.HRBP] = new[] { DomainPermissions.Employee.View, DomainPermissions.EmploymentHistory.View, DomainPermissions.Leave.Approve, DomainPermissions.Separation.ViewAll, DomainPermissions.Separation.HrReview, DomainPermissions.Separation.ReviseLastWorkingDate, DomainPermissions.Separation.Reject, DomainPermissions.Separation.ViewHistory }
        ,[RoleNames.TimeManager] = new[] { DomainPermissions.Employee.View, DomainPermissions.Attendance.View }
        ,[RoleNames.IT] = new[] { DomainPermissions.User.View, DomainPermissions.User.Edit, DomainPermissions.AccountEmployeeLink.View }
        ,[RoleNames.Accounts] = new[] { DomainPermissions.Employee.View }
        ,[RoleNames.SuperHR] = new[] { DomainPermissions.Employee.View, DomainPermissions.Employee.Edit, DomainPermissions.EmploymentHistory.View, DomainPermissions.EmploymentHistory.Change, DomainPermissions.Leave.Approve, DomainPermissions.RoleManagement.View, DomainPermissions.RoleManagement.AssignmentView, DomainPermissions.PageAccess.View, DomainPermissions.PageAccess.Manage, DomainPermissions.Payroll.SalaryComponentView, DomainPermissions.Payroll.SalaryComponentManage, DomainPermissions.Payroll.SalaryComponentViewHistory, DomainPermissions.Payroll.SalaryStructureView, DomainPermissions.Payroll.SalaryStructureManage, DomainPermissions.Payroll.SalaryStructureViewHistory, DomainPermissions.Payroll.EmployeeSalaryView, DomainPermissions.Payroll.EmployeeSalaryManage, DomainPermissions.Payroll.EmployeeSalaryViewHistory, DomainPermissions.Payroll.PeriodView, DomainPermissions.Payroll.PeriodManage, DomainPermissions.Payroll.PeriodUnlock, DomainPermissions.Payroll.ControlsView, DomainPermissions.Payroll.ControlsManage, DomainPermissions.Payroll.RunView, DomainPermissions.Payroll.RunManage, DomainPermissions.Payroll.RunPrepare, DomainPermissions.Payroll.RunApprove, DomainPermissions.Payroll.RunFinalize, DomainPermissions.Payroll.RunViewHistory, DomainPermissions.Payroll.RunCalculate, DomainPermissions.Payroll.RunRecalculate, DomainPermissions.Payroll.RunViewResults }
    };

    /// <summary>
    /// The demo organizations, as the catalog knows them.
    /// <para>
    /// The hosts are <c>*.localhost</c> because every label under <c>localhost</c> resolves to the loopback
    /// address without any hosts-file entry on Windows, macOS and Linux — so
    /// <c>http://demo01.localhost:5173</c> works on a fresh clone, and whitelabel routing can be exercised
    /// in development the same way it will run in production instead of being stubbed out.
    /// </para>
    /// <para>
    /// <see cref="Tenant.ShardKey"/> matches the lowercased code here, which is a convenience of the demo
    /// data and not a rule: the key is looked up in configuration to find a connection string, so a real
    /// tenant can be moved to another database by changing its key without touching its code or host.
    /// </para>
    /// </summary>
    public static IReadOnlyList<Tenant> Tenants => new List<Tenant>
    {
        new()
        {
            Id = TenantIds.Demo01,
            TenantCode = "DEMO01",
            Host = "demo01.localhost",
            ShardKey = "demo01",
            TenantName = "Demo Organization",
            Email = "contact@demo01.com",
            Phone = "+1-555-0100",
            Address = "100 Demo Street",
            Status = TenantStatus.Active
        },
        new()
        {
            Id = TenantIds.Demo02,
            TenantCode = "DEMO02",
            Host = "demo02.localhost",
            ShardKey = "demo02",
            TenantName = "Sample Organization",
            Email = "contact@demo02.com",
            Phone = "+1-555-0200",
            Address = "200 Sample Avenue",
            Status = TenantStatus.Active
        }
    };

    /// <summary>
    /// Sign-in branding for the demo tenants.
    /// <para>
    /// Both opt in via <see cref="TenantBranding.IsPublic"/> so the tenant-aware login screen has
    /// something real to show in development, and their accent colours are deliberately unlike each
    /// other <em>and</em> unlike the product accent — opening one organization's address and then the
    /// other visibly re-themes the page, which is the fastest way to see that branding follows the host
    /// rather than being hard-coded.
    /// </para>
    /// <para>
    /// <see cref="TenantBranding.LogoUrl"/> is null on purpose. A seeded URL would either point at an
    /// asset that does not exist or make development depend on a third-party host, so the client draws a
    /// monogram from the display name instead. <see cref="TenantBranding.SsoEnabled"/> is false because
    /// no identity provider is implemented; setting it true would advertise a route that goes nowhere.
    /// </para>
    /// </summary>
    public static IReadOnlyList<TenantBranding> Branding => new List<TenantBranding>
    {
        new()
        {
            TenantId = TenantIds.Demo01,
            IsPublic = true,
            DisplayName = "Demo Organization",
            LogoUrl = null,
            PrimaryColor = "#0F766E",
            WelcomeMessage = "Sign in to the Demo Organization workspace.",
            SupportEmail = "itsupport@demo01.com",
            SsoEnabled = false,
            SsoProviderName = null
        },
        new()
        {
            TenantId = TenantIds.Demo02,
            IsPublic = true,
            DisplayName = "Sample Organization",
            LogoUrl = null,
            PrimaryColor = "#7C3AED",
            WelcomeMessage = "Sign in to the Sample Organization workspace.",
            SupportEmail = "itsupport@demo02.com",
            SsoEnabled = false,
            SsoProviderName = null
        }
    };

    public static IReadOnlyList<SeedUser> Users => new List<SeedUser>
    {
        new(new("a1111111-1111-1111-1111-111111111111"), TenantIds.Demo01, "admin@demo01.com", "Alice", "Admin", RoleNames.TenantAdmin),
        new(new("a2222222-2222-2222-2222-222222222222"), TenantIds.Demo01, "hr@demo01.com", "Henry", "Human", RoleNames.HRManager),
        new(new("b1111111-1111-1111-1111-111111111111"), TenantIds.Demo02, "admin@demo02.com", "Bob", "Admin", RoleNames.TenantAdmin),
        new(new("b2222222-2222-2222-2222-222222222222"), TenantIds.Demo02, "hr@demo02.com", "Hana", "Resource", RoleNames.HRManager)
    };

    /// <summary>
    /// Departments per tenant. The leading digit of each id encodes the tenant (1… = DEMO01, 2… = DEMO02),
    /// which makes a cross-tenant mistake visible at a glance in test output and log lines.
    /// </summary>
    public static IReadOnlyList<SeedDepartment> Departments => new List<SeedDepartment>
    {
        new(new("10000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "ENG", "Engineering", "Product engineering and platform."),
        new(new("10000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "HR", "Human Resources", "People operations and recruitment."),
        new(new("10000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "FIN", "Finance", "Accounting, payroll and reporting."),
        new(new("20000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "OPS", "Operations", "Service delivery and logistics."),
        new(new("20000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "SLS", "Sales", "New business and account management.")
    };

    /// <summary>
    /// Banks per tenant, used to populate the bank master dropdown for employee bank details. One bank is
    /// seeded inactive in each tenant so the inactive-handling behaviour can be exercised.
    /// </summary>
    public static IReadOnlyList<SeedBank> Banks => new List<SeedBank>
    {
        new(new("13000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "SBI", "State Bank of India", "Largest public sector bank.", true),
        new(new("13000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "HDFC", "HDFC Bank", "Private sector bank.", true),
        new(new("13000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "ICICI", "ICICI Bank", "Private sector bank.", true),
        new(new("13000000-0000-0000-0000-000000000004"), TenantIds.Demo01, "AXIS", "Axis Bank", "Private sector bank (inactive).", false),
        new(new("23000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "BOB", "Bank of Baroda", "Public sector bank.", true),
        new(new("23000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "PNB", "Punjab National Bank", "Public sector bank (inactive).", false)
    };

    public static IReadOnlyList<SeedDesignation> Designations => new List<SeedDesignation>
    {
        new(new("11000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "CTO", "Chief Technology Officer", "Leads the technology organization."),
        new(new("11000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "EM", "Engineering Manager", "Leads an engineering team."),
        new(new("11000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "SSE", "Senior Software Engineer", "Senior individual contributor."),
        new(new("11000000-0000-0000-0000-000000000004"), TenantIds.Demo01, "SE", "Software Engineer", "Individual contributor."),
        new(new("11000000-0000-0000-0000-000000000005"), TenantIds.Demo01, "HRM", "HR Manager", "Leads people operations."),
        new(new("11000000-0000-0000-0000-000000000006"), TenantIds.Demo01, "ACC", "Accountant", "Bookkeeping and reporting."),
        new(new("21000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "OPSM", "Operations Manager", "Leads service delivery."),
        new(new("21000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "SR", "Sales Representative", "Field sales.")
    };

    /// <summary>
    /// Demo employees, including a small reporting hierarchy so the manager relationship (and the loop
    /// check that guards it) has something real to run against. The two tenants deliberately have
    /// different sizes and unrelated names, which makes a leak between them obvious rather than subtle.
    /// </summary>
    public static IReadOnlyList<SeedEmployee> Employees => new List<SeedEmployee>
    {
        new(new("12000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "EMP-001", "Nadia", "Farrell",
            "nadia.farrell@demo01.com", "555-0101", new DateOnly(1980, 4, 12), Gender.Female,
            new DateOnly(2015, 1, 5), "ENG", "CTO", null, "12 Bridge Road"),
        new(new("12000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "EMP-002", "Owen", "Brand",
            "owen.brand@demo01.com", "555-0102", new DateOnly(1986, 9, 30), Gender.Male,
            new DateOnly(2017, 3, 20), "ENG", "EM", "EMP-001", "8 Mill Lane"),
        new(new("12000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "EMP-003", "Priya", "Raman",
            "priya.raman@demo01.com", "555-0103", new DateOnly(1991, 2, 18), Gender.Female,
            new DateOnly(2019, 7, 1), "ENG", "SSE", "EMP-002", "44 Orchard Street"),
        new(new("12000000-0000-0000-0000-000000000004"), TenantIds.Demo01, "EMP-004", "Diego", "Santos",
            "diego.santos@demo01.com", "555-0104", new DateOnly(1995, 11, 7), Gender.Male,
            new DateOnly(2022, 2, 14), "ENG", "SE", "EMP-002", "3 Harbour View"),
        new(new("12000000-0000-0000-0000-000000000005"), TenantIds.Demo01, "EMP-005", "Mira", "Kovac",
            "mira.kovac@demo01.com", "555-0105", new DateOnly(1988, 6, 25), Gender.Female,
            new DateOnly(2018, 5, 2), "HR", "HRM", "EMP-001", "77 Elm Avenue"),
        new(new("12000000-0000-0000-0000-000000000006"), TenantIds.Demo01, "EMP-006", "Tomas", "Lind",
            "tomas.lind@demo01.com", "555-0106", new DateOnly(1983, 8, 9), Gender.Male,
            new DateOnly(2016, 10, 17), "FIN", "ACC", "EMP-001", "5 Castle Terrace"),
        new(new("22000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "E-100", "Grace", "Okoro",
            "grace.okoro@demo02.com", "555-0201", new DateOnly(1984, 12, 1), Gender.Female,
            new DateOnly(2016, 4, 11), "OPS", "OPSM", null, "21 Sample Way"),
            new(new("22000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "E-101", "Liam", "Hayes",
            "liam.hayes@demo02.com", "555-0202", new DateOnly(1993, 5, 22), Gender.Male,
            new DateOnly(2021, 9, 6), "SLS", "SR", "E-100", "9 Sample Close")
    };

    public record SeedCountry(Guid Id, string Code, string Name);
    public record SeedState(Guid Id, Guid CountryId, string Code, string Name);
    public record SeedCity(Guid Id, Guid StateId, string Code, string Name);

    /// <summary>A position change reason to be seeded.</summary>
    public record SeedPositionChangeReason(Guid Id, Guid TenantId, string Code, string Name, string? Description, int SortOrder);

    private static readonly Guid IndiaId = new("30000000-0000-0000-0000-000000000001");
    private static readonly Guid USAId = new("30000000-0000-0000-0000-000000000002");
    private static readonly Guid UAEId = new("30000000-0000-0000-0000-000000000003");
    private static readonly Guid UKId = new("30000000-0000-0000-0000-000000000004");
    private static readonly Guid CanadaId = new("30000000-0000-0000-0000-000000000005");
    private static readonly Guid AustraliaId = new("30000000-0000-0000-0000-000000000006");

    public static IReadOnlyList<SeedCountry> Countries => new List<SeedCountry>
    {
        new(IndiaId, "IN", "India"),
        new(USAId, "US", "United States"),
        new(UAEId, "AE", "United Arab Emirates"),
        new(UKId, "GB", "United Kingdom"),
        new(CanadaId, "CA", "Canada"),
        new(AustraliaId, "AU", "Australia")
    };

    public static IReadOnlyList<SeedState> States => new List<SeedState>
    {
        // India
        new(new("31000000-0000-0000-0000-000000000001"), IndiaId, "AP", "Andhra Pradesh"),
        new(new("31000000-0000-0000-0000-000000000002"), IndiaId, "AR", "Arunachal Pradesh"),
        new(new("31000000-0000-0000-0000-000000000003"), IndiaId, "AS", "Assam"),
        new(new("31000000-0000-0000-0000-000000000004"), IndiaId, "BR", "Bihar"),
        new(new("31000000-0000-0000-0000-000000000005"), IndiaId, "CG", "Chhattisgarh"),
        new(new("31000000-0000-0000-0000-000000000006"), IndiaId, "GA", "Goa"),
        new(new("31000000-0000-0000-0000-000000000007"), IndiaId, "GJ", "Gujarat"),
        new(new("31000000-0000-0000-0000-000000000008"), IndiaId, "HR", "Haryana"),
        new(new("31000000-0000-0000-0000-000000000009"), IndiaId, "HP", "Himachal Pradesh"),
        new(new("31000000-0000-0000-0000-000000000010"), IndiaId, "JH", "Jharkhand"),
        new(new("31000000-0000-0000-0000-000000000011"), IndiaId, "KA", "Karnataka"),
        new(new("31000000-0000-0000-0000-000000000012"), IndiaId, "KL", "Kerala"),
        new(new("31000000-0000-0000-0000-000000000013"), IndiaId, "MP", "Madhya Pradesh"),
        new(new("31000000-0000-0000-0000-000000000014"), IndiaId, "MH", "Maharashtra"),
        new(new("31000000-0000-0000-0000-000000000015"), IndiaId, "MN", "Manipur"),
        new(new("31000000-0000-0000-0000-000000000016"), IndiaId, "ML", "Meghalaya"),
        new(new("31000000-0000-0000-0000-000000000017"), IndiaId, "MZ", "Mizoram"),
        new(new("31000000-0000-0000-0000-000000000018"), IndiaId, "NL", "Nagaland"),
        new(new("31000000-0000-0000-0000-000000000019"), IndiaId, "OD", "Odisha"),
        new(new("31000000-0000-0000-0000-000000000020"), IndiaId, "PB", "Punjab"),
        new(new("31000000-0000-0000-0000-000000000021"), IndiaId, "RJ", "Rajasthan"),
        new(new("31000000-0000-0000-0000-000000000022"), IndiaId, "SK", "Sikkim"),
        new(new("31000000-0000-0000-0000-000000000023"), IndiaId, "TN", "Tamil Nadu"),
        new(new("31000000-0000-0000-0000-000000000024"), IndiaId, "TS", "Telangana"),
        new(new("31000000-0000-0000-0000-000000000025"), IndiaId, "TR", "Tripura"),
        new(new("31000000-0000-0000-0000-000000000026"), IndiaId, "UP", "Uttar Pradesh"),
        new(new("31000000-0000-0000-0000-000000000027"), IndiaId, "UT", "Uttarakhand"),
        new(new("31000000-0000-0000-0000-000000000028"), IndiaId, "WB", "West Bengal"),
        new(new("31000000-0000-0000-0000-000000000029"), IndiaId, "DL", "Delhi"),
        // USA
        new(new("31000000-0000-0000-0000-000000000030"), USAId, "CA", "California"),
        new(new("31000000-0000-0000-0000-000000000031"), USAId, "NY", "New York"),
        new(new("31000000-0000-0000-0000-000000000032"), USAId, "TX", "Texas"),
        new(new("31000000-0000-0000-0000-000000000033"), USAId, "FL", "Florida"),
        new(new("31000000-0000-0000-0000-000000000034"), USAId, "IL", "Illinois"),
        // UAE
        new(new("31000000-0000-0000-0000-000000000035"), UAEId, "DXB", "Dubai"),
        new(new("31000000-0000-0000-0000-000000000036"), UAEId, "AUH", "Abu Dhabi"),
        new(new("31000000-0000-0000-0000-000000000037"), UAEId, "SHJ", "Sharjah"),
        // UK
        new(new("31000000-0000-0000-0000-000000000038"), UKId, "ENG", "England"),
        new(new("31000000-0000-0000-0000-000000000039"), UKId, "SCT", "Scotland"),
        new(new("31000000-0000-0000-0000-000000000040"), UKId, "WLS", "Wales"),
        // Canada
        new(new("31000000-0000-0000-0000-000000000041"), CanadaId, "ON", "Ontario"),
        new(new("31000000-0000-0000-0000-000000000042"), CanadaId, "BC", "British Columbia"),
        new(new("31000000-0000-0000-0000-000000000043"), CanadaId, "AB", "Alberta"),
        // Australia
        new(new("31000000-0000-0000-0000-000000000044"), AustraliaId, "NSW", "New South Wales"),
        new(new("31000000-0000-0000-0000-000000000045"), AustraliaId, "VIC", "Victoria"),
        new(new("31000000-0000-0000-0000-000000000046"), AustraliaId, "QLD", "Queensland"),
    };

    private static readonly Guid India_MH = new("31000000-0000-0000-0000-000000000014");
    private static readonly Guid India_KA = new("31000000-0000-0000-0000-000000000011");
    private static readonly Guid India_DL = new("31000000-0000-0000-0000-000000000029");
    private static readonly Guid India_GJ = new("31000000-0000-0000-0000-000000000007");
    private static readonly Guid India_TN = new("31000000-0000-0000-0000-000000000023");
    private static readonly Guid India_Telangana = new("31000000-0000-0000-0000-000000000024");
    private static readonly Guid India_UP = new("31000000-0000-0000-0000-000000000026");
    private static readonly Guid India_WB = new("31000000-0000-0000-0000-000000000028");
    private static readonly Guid India_RJ = new("31000000-0000-0000-0000-000000000021");
    private static readonly Guid India_HR = new("31000000-0000-0000-0000-000000000008");
    private static readonly Guid India_PB = new("31000000-0000-0000-0000-000000000020");

    private static readonly Guid USA_CA = new("31000000-0000-0000-0000-000000000030");
    private static readonly Guid USA_NY = new("31000000-0000-0000-0000-000000000031");
    private static readonly Guid UAE_DX = new("31000000-0000-0000-0000-000000000035");
    private static readonly Guid UK_ENG = new("31000000-0000-0000-0000-000000000038");

    public static IReadOnlyList<SeedCity> Cities => new List<SeedCity>
    {
        // Maharashtra
        new(new("32000000-0000-0000-0000-000000000001"), India_MH, "MUM", "Mumbai"),
        new(new("32000000-0000-0000-0000-000000000002"), India_MH, "PUN", "Pune"),
        new(new("32000000-0000-0000-0000-000000000003"), India_MH, "NGP", "Nagpur"),
        // Karnataka
        new(new("32000000-0000-0000-0000-000000000004"), India_KA, "BLR", "Bengaluru"),
        new(new("32000000-0000-0000-0000-000000000005"), India_KA, "MYS", "Mysuru"),
        // Delhi
        new(new("32000000-0000-0000-0000-000000000006"), India_DL, "NDL", "New Delhi"),
        // Gujarat
        new(new("32000000-0000-0000-0000-000000000007"), India_GJ, "AMD", "Ahmedabad"),
        new(new("32000000-0000-0000-0000-000000000008"), India_GJ, "SRT", "Surat"),
        // Tamil Nadu
        new(new("32000000-0000-0000-0000-000000000009"), India_TN, "CHN", "Chennai"),
        new(new("32000000-0000-0000-0000-000000000010"), India_TN, "COI", "Coimbatore"),
        // Telangana
        new(new("32000000-0000-0000-0000-000000000011"), India_Telangana, "HYD", "Hyderabad"),
        // Uttar Pradesh
        new(new("32000000-0000-0000-0000-000000000012"), India_UP, "LKO", "Lucknow"),
        new(new("32000000-0000-0000-0000-000000000013"), India_UP, "AGR", "Agra"),
        // West Bengal
        new(new("32000000-0000-0000-0000-000000000014"), India_WB, "KOL", "Kolkata"),
        // Rajasthan
        new(new("32000000-0000-0000-0000-000000000015"), India_RJ, "JAI", "Jaipur"),
        // Haryana
        new(new("32000000-0000-0000-0000-000000000016"), India_HR, "GUR", "Gurugram"),
        // Punjab
        new(new("32000000-0000-0000-0000-000000000017"), India_PB, "CDR", "Chandigarh"),
        // USA
        new(new("32000000-0000-0000-0000-000000000018"), USA_CA, "SFO", "San Francisco"),
        new(new("32000000-0000-0000-0000-000000000019"), USA_CA, "LAX", "Los Angeles"),
        new(new("32000000-0000-0000-0000-000000000020"), USA_NY, "NYC", "New York City"),
        // UAE
        new(new("32000000-0000-0000-0000-000000000021"), UAE_DX, "DXB", "Dubai City"),
        // UK
        new(new("32000000-0000-0000-0000-000000000022"), UK_ENG, "LON", "London"),
    };

    /// <summary>
    /// Default position change reasons, seeded per tenant. These mirror the old EmploymentChangeReason
    /// enum values but are now tenant-scoped master records that tenants may extend.
    /// </summary>
    public static IReadOnlyList<SeedPositionChangeReason> PositionChangeReasons => new List<SeedPositionChangeReason>
    {
        new(new("40000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "INITIAL", "Initial Position", "Original hiring position.", 1),
        new(new("40000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "PROMO", "Promotion", "Grade or designation upgrade.", 2),
        new(new("40000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "TRANSFER", "Transfer", "Lateral transfer to another unit.", 3),
        new(new("40000000-0000-0000-0000-000000000004"), TenantIds.Demo01, "DEPT_CHG", "Department Change", "Moved to a different department.", 4),
        new(new("40000000-0000-0000-0000-000000000005"), TenantIds.Demo01, "DESIG_CHG", "Designation Change", "Changed job title/designation.", 5),
        new(new("40000000-0000-0000-0000-000000000006"), TenantIds.Demo01, "GRADE_CHG", "Grade Change", "Grade level adjusted.", 6),
        new(new("40000000-0000-0000-0000-000000000007"), TenantIds.Demo01, "LOC_CHG", "Location Change", "Work location changed.", 7),
        new(new("40000000-0000-0000-0000-000000000008"), TenantIds.Demo01, "MGR_CHG", "Manager Change", "Reporting manager changed.", 8),
        new(new("40000000-0000-0000-0000-000000000009"), TenantIds.Demo01, "RESTRUCTURE", "Organizational Restructure", "Change due to org restructuring.", 9),
        new(new("40000000-0000-0000-0000-000000000010"), TenantIds.Demo01, "CORRECTION", "Correction of Employment", "Data correction of an employment transaction.", 10),
        new(new("40000000-0000-0000-0000-000000000011"), TenantIds.Demo01, "DEMOTE", "Demotion", "Grade or designation downgrade.", 11),
        new(new("40000000-0000-0000-0000-000000000012"), TenantIds.Demo01, "OTHER", "Other", "Other reason not listed above.", 12),
        new(new("40000000-0000-0000-0000-000000000013"), TenantIds.Demo01, "NEW_HIRE", "New Hire", "Initial appointment of a new employee.", 0),
        new(new("40000000-0000-0000-0000-000000000014"), TenantIds.Demo01, "RETIRE", "Retirement", "Employee retired from service.", 13),

        new(new("41000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "INITIAL", "Initial Position", "Original hiring position.", 1),
        new(new("41000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "PROMO", "Promotion", "Grade or designation upgrade.", 2),
        new(new("41000000-0000-0000-0000-000000000003"), TenantIds.Demo02, "TRANSFER", "Transfer", "Lateral transfer to another unit.", 3),
        new(new("41000000-0000-0000-0000-000000000004"), TenantIds.Demo02, "DEPT_CHG", "Department Change", "Moved to a different department.", 4),
        new(new("41000000-0000-0000-0000-000000000005"), TenantIds.Demo02, "DESIG_CHG", "Designation Change", "Changed job title/designation.", 5),
        new(new("41000000-0000-0000-0000-000000000006"), TenantIds.Demo02, "GRADE_CHG", "Grade Change", "Grade level adjusted.", 6),
        new(new("41000000-0000-0000-0000-000000000007"), TenantIds.Demo02, "LOC_CHG", "Location Change", "Work location changed.", 7),
        new(new("41000000-0000-0000-0000-000000000008"), TenantIds.Demo02, "MGR_CHG", "Manager Change", "Reporting manager changed.", 8),
        new(new("41000000-0000-0000-0000-000000000009"), TenantIds.Demo02, "RESTRUCTURE", "Organizational Restructure", "Change due to org restructuring.", 9),
        new(new("41000000-0000-0000-0000-000000000010"), TenantIds.Demo02, "CORRECTION", "Correction of Employment", "Data correction of an employment transaction.", 10),
        new(new("41000000-0000-0000-0000-000000000011"), TenantIds.Demo02, "DEMOTE", "Demotion", "Grade or designation downgrade.", 11),
        new(new("41000000-0000-0000-0000-000000000012"), TenantIds.Demo02, "OTHER", "Other", "Other reason not listed above.", 12),
        new(new("41000000-0000-0000-0000-000000000013"), TenantIds.Demo02, "NEW_HIRE", "New Hire", "Initial appointment of a new employee.", 0),
        new(new("41000000-0000-0000-0000-000000000014"), TenantIds.Demo02, "RETIRE", "Retirement", "Employee retired from service.", 13)
    };

    // ── Organizational hierarchy masters ───────────────────────────────────────

    /// <summary>Holding companies per tenant.</summary>
    public static IReadOnlyList<SeedHoldingCompany> HoldingCompanies => new List<SeedHoldingCompany>
    {
        new(new("14000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "HC01", "Acme Global Holdings", "Top-level holding company.", true),
        new(new("24000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "HC01", "Sample Global Holdings", "Top-level holding company.", true)
    };

    /// <summary>Lines of business, optionally parented to a holding company.</summary>
    public static IReadOnlyList<SeedLob> LinesOfBusiness => new List<SeedLob>
    {
        new(new("14100000-0000-0000-0000-000000000001"), TenantIds.Demo01, "LOB-IT", "IT Services", "Technology services line.", true,
            new("14000000-0000-0000-0000-000000000001")),
        new(new("24100000-0000-0000-0000-000000000001"), TenantIds.Demo02, "LOB-OPS", "Operations", "Service operations line.", true,
            new("24000000-0000-0000-0000-000000000001"))
    };

    /// <summary>Organizational units (legal entities / subsidiaries), flat master.</summary>
    public static IReadOnlyList<SeedOrganisation> Organisations => new List<SeedOrganisation>
    {
        new(new("14200000-0000-0000-0000-000000000001"), TenantIds.Demo01, "ORG01", "Acme Technologies Pvt Ltd", "Primary legal entity.", true),
        new(new("24200000-0000-0000-0000-000000000001"), TenantIds.Demo02, "ORG01", "Sample Tech Private Ltd", "Primary legal entity.", true)
    };

    /// <summary>Sub-departments, parented to a department.</summary>
    public static IReadOnlyList<SeedSubDepartment> SubDepartments => new List<SeedSubDepartment>
    {
        new(new("14300000-0000-0000-0000-000000000001"), TenantIds.Demo01, "SUB-PLAT", "Platform Engineering", "Shared platform team.", true,
            new("10000000-0000-0000-0000-000000000001")),
        new(new("24300000-0000-0000-0000-000000000001"), TenantIds.Demo02, "SUB-FLD", "Field Operations", "On-site service team.", true,
            new("20000000-0000-0000-0000-000000000001"))
    };

    /// <summary>Sections, optionally parented to a sub-department.</summary>
    public static IReadOnlyList<SeedSection> Sections => new List<SeedSection>
    {
        new(new("14400000-0000-0000-0000-000000000001"), TenantIds.Demo01, "SEC-CORE", "Core Platform", "Core platform sub-team.", true,
            new("14300000-0000-0000-0000-000000000001")),
        new(new("24400000-0000-0000-0000-000000000001"), TenantIds.Demo02, "SEC-LOG", "Logistics", "Logistics sub-team.", true,
            new("24300000-0000-0000-0000-000000000001"))
    };

    /// <summary>Sub-sections, optionally parented to a section.</summary>
    public static IReadOnlyList<SeedSubSection> SubSections => new List<SeedSubSection>
    {
        new(new("14500000-0000-0000-0000-000000000001"), TenantIds.Demo01, "SS-PAY", "Payments", "Payments squad.", true,
            new("14400000-0000-0000-0000-000000000001")),
        new(new("24500000-0000-0000-0000-000000000001"), TenantIds.Demo02, "SS-ROUTE", "Routing", "Dispatch routing squad.", true,
            new("24400000-0000-0000-0000-000000000001"))
    };

    /// <summary>Business functions, flat master.</summary>
    public static IReadOnlyList<SeedFunction> Functions => new List<SeedFunction>
    {
        new(new("14600000-0000-0000-0000-000000000001"), TenantIds.Demo01, "FN-ENG", "Engineering", "Engineering function.", true),
        new(new("24600000-0000-0000-0000-000000000001"), TenantIds.Demo02, "FN-OPS", "Operations", "Operations function.", true)
    };

    /// <summary>Sub-functions, optionally parented to a function.</summary>
    public static IReadOnlyList<SeedSubFunction> SubFunctions => new List<SeedSubFunction>
    {
        new(new("14700000-0000-0000-0000-000000000001"), TenantIds.Demo01, "SF-BE", "Backend", "Backend engineering.", true,
            new("14600000-0000-0000-0000-000000000001")),
        new(new("24700000-0000-0000-0000-000000000001"), TenantIds.Demo02, "SF-FIELD", "Field Service", "Field service delivery.", true,
            new("24600000-0000-0000-0000-000000000001"))
    };

    /// <summary>Pay grades, flat master sorted by <see cref="Grade.SortOrder"/>.</summary>
    public static IReadOnlyList<SeedGrade> Grades => new List<SeedGrade>
    {
        new(new("14800000-0000-0000-0000-000000000001"), TenantIds.Demo01, "G1", "Grade 1", "Entry level.", true, 1),
        new(new("14800000-0000-0000-0000-000000000002"), TenantIds.Demo01, "G2", "Grade 2", "Mid level.", true, 2),
        new(new("14800000-0000-0000-0000-000000000003"), TenantIds.Demo01, "G3", "Grade 3", "Senior level.", true, 3),
        new(new("24800000-0000-0000-0000-000000000001"), TenantIds.Demo02, "G1", "Grade 1", "Entry level.", true, 1),
        new(new("24800000-0000-0000-0000-000000000002"), TenantIds.Demo02, "G2", "Grade 2", "Mid level.", true, 2),
        new(new("24800000-0000-0000-0000-000000000003"), TenantIds.Demo02, "G3", "Grade 3", "Senior level.", true, 3)
    };

    /// <summary>Physical work locations per tenant.</summary>
    public static IReadOnlyList<SeedWorkLocation> WorkLocations => new List<SeedWorkLocation>
    {
        new(new("14900000-0000-0000-0000-000000000001"), TenantIds.Demo01, "WL-MUM", "Mumbai Office", "Mumbai headquarters.", true),
        new(new("14900000-0000-0000-0000-000000000002"), TenantIds.Demo01, "WL-BLR", "Bengaluru Office", "Bengaluru engineering office.", true),
        new(new("24900000-0000-0000-0000-000000000001"), TenantIds.Demo02, "WL-DXB", "Dubai Office", "Dubai headquarters.", true)
    };

    /// <summary>Employment contract types, sorted by <see cref="EmployeeType.SortOrder"/>.</summary>
    public static IReadOnlyList<SeedEmployeeType> EmployeeTypes => new List<SeedEmployeeType>
    {
        new(new("15000000-0000-0000-0000-000000000001"), TenantIds.Demo01, "FT", "Full Time", "Standard full-time employment.", true, 1),
        new(new("15000000-0000-0000-0000-000000000002"), TenantIds.Demo01, "CT", "Contract", "Fixed-term contract.", true, 2),
        new(new("15000000-0000-0000-0000-000000000003"), TenantIds.Demo01, "INT", "Intern", "Internship.", true, 3),
        new(new("15000000-0000-0000-0000-000000000004"), TenantIds.Demo01, "PT", "Part Time", "Part-time schedule (inactive).", false, 4),
        new(new("25000000-0000-0000-0000-000000000001"), TenantIds.Demo02, "FT", "Full Time", "Standard full-time employment.", true, 1),
        new(new("25000000-0000-0000-0000-000000000002"), TenantIds.Demo02, "CT", "Contract", "Fixed-term contract.", true, 2),
        new(new("25000000-0000-0000-0000-000000000003"), TenantIds.Demo02, "INT", "Intern", "Internship.", true, 3)
    };

    /// <summary>Cost centers per tenant for financial allocation.</summary>
    public static IReadOnlyList<SeedCostCenter> CostCenters => new List<SeedCostCenter>
    {
        new(new("15100000-0000-0000-0000-000000000001"), TenantIds.Demo01, "CC-ENG", "Engineering", "Product and platform engineering.", true),
        new(new("15100000-0000-0000-0000-000000000002"), TenantIds.Demo01, "CC-HR", "Human Resources", "People operations and recruitment.", true),
        new(new("15100000-0000-0000-0000-000000000003"), TenantIds.Demo01, "CC-FIN", "Finance", "Accounting, payroll and reporting.", true),
        new(new("15100000-0000-0000-0000-000000000004"), TenantIds.Demo01, "CC-RND", "Research & Development", "R&D programs.", true),
        new(new("25100000-0000-0000-0000-000000000001"), TenantIds.Demo02, "CC-OPS", "Operations", "Service delivery and logistics.", true),
        new(new("25100000-0000-0000-0000-000000000002"), TenantIds.Demo02, "CC-SLS", "Sales", "New business and account management.", true)
    };
}
