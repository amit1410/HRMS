using FluentValidation;
using HRMS.Application.Abstractions;
using HRMS.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HRMS.Application;

/// <summary>
/// DI registration for the Application layer: FluentValidation validators discovered in this assembly,
/// the application services that hold business logic, and the clock abstraction they use.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        // Injected rather than calling DateTime.UtcNow, so token lifetimes and audit stamps are testable.
        services.TryAddSingletonTimeProvider();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IPasswordRecoveryService, PasswordRecoveryService>();
        services.AddScoped<IAccountEmployeeLinkService, AccountEmployeeLinkService>();
        services.AddScoped<IManagerRoleProvisioningService, ManagerRoleProvisioningService>();
        services.AddScoped<IEmployeeRoleProvisioningService, EmployeeRoleProvisioningService>();
        services.AddScoped<IRoleAssignmentService, RoleAssignmentService>();
        services.AddScoped<IRoleResolutionService, RoleResolutionService>();
        services.AddScoped<IRoleScopeResolver, RoleScopeResolver>();
        services.AddScoped<IPageAccessService, PageAccessService>();
        services.AddScoped<ITenantBrandingService, TenantBrandingService>();
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IDesignationService, DesignationService>();
        services.AddScoped<ICountryService, CountryService>();
        services.AddScoped<IStateService, StateService>();
        services.AddScoped<ICityService, CityService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IEmployeeAccessScopeService, EmployeeAccessScopeService>();
        services.AddScoped<ILeaveAuthorizationService, LeaveAuthorizationService>();
        services.AddScoped<IAttendanceAuthorizationService, AttendanceAuthorizationService>();
        services.AddScoped<IEmployeePortalAccountService, EmployeePortalAccountService>();
        services.AddScoped<IEmployeeCodeConfigurationService, EmployeeCodeConfigurationService>();
        services.AddScoped<IEmployeeCodeSequenceService, EmployeeCodeSequenceService>();
        services.AddSingleton<EmployeeCodes.EmployeeCodeRuleMatcher>();
        services.AddSingleton<EmployeeCodes.EmployeeCodeRenderer>();

        // Employee sub-entity services
        services.AddScoped<IEmployeeContactService, EmployeeContactService>();
        services.AddScoped<IEmployeeAddressService, EmployeeAddressService>();
        services.AddScoped<IEmployeeFamilyService, EmployeeFamilyService>();
        services.AddScoped<IEmployeeEducationService, EmployeeEducationService>();
        services.AddScoped<IEmployeePreviousEmploymentService, EmployeePreviousEmploymentService>();
        services.AddScoped<IEmployeeBankDetailService, EmployeeBankDetailService>();
        services.AddScoped<IEmployeeSupervisorService, EmployeeSupervisorService>();
        services.AddScoped<IEmployeeManagerResolver, EmployeeManagerResolver>();
        services.AddScoped<IEmployeeAdditionalInfoService, EmployeeAdditionalInfoService>();
        services.AddScoped<IEmployeeEmploymentService, EmployeeEmploymentService>();
        services.AddScoped<IEmployeeAuditService, EmployeeAuditService>();
        services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();
        services.AddScoped<ILeavePolicyFoundationService, LeavePolicyFoundationService>();
        services.AddScoped<ILeavePolicyResolver, LeavePolicyResolver>();
        services.AddScoped<ILeavePeriodResolver, LeavePeriodResolver>();
        services.AddScoped<ILeaveBalanceTransactionPoster, LeaveBalanceTransactionPoster>();
        services.AddScoped<ILeaveBalanceReader, LeaveBalanceReader>();
        services.AddScoped<ILeaveBalanceSummaryReader, LeaveBalanceSummaryReader>();
        services.AddScoped<ILeaveBalanceImportService, LeaveBalanceImportService>();
        services.AddScoped<ILeaveAccrualProcessor, LeaveAccrualProcessor>();
        services.AddScoped<ILeavePeriodCloseProcessor, LeavePeriodCloseProcessor>();
        services.AddScoped<ILeaveEntitlementExpiryProcessor, LeaveEntitlementExpiryProcessor>();
        services.AddScoped<ILeaveBalanceAccountingService, LeaveBalanceAccountingService>();
        services.AddScoped<IEmployeeIdentityResolver, EmployeeIdentityResolver>();
        services.AddScoped<IEffectiveEmploymentResolver, EffectiveEmploymentResolver>();
        services.AddScoped<IWorkingDayCalendarResolver, WorkingDayCalendarResolver>();
        services.AddScoped<ILeaveWorkingDayConfigurationService, LeaveWorkingDayConfigurationService>();
        services.AddScoped<IMyEmployeeProfileService, MyEmployeeProfileService>();
        services.AddScoped<ILeaveConfigurationService, LeaveConfigurationService>();
        services.AddScoped<ILeaveRequestValidationService, LeaveRequestValidationService>();
        services.AddScoped<ILeaveRequestSubmissionService, LeaveRequestSubmissionService>();
        services.AddScoped<ILeaveRequestApprovalService, LeaveRequestApprovalService>();
        services.AddScoped<ILeaveRequestWithdrawalService, LeaveRequestWithdrawalService>();
        services.AddScoped<ILeaveRequestCancellationService, LeaveRequestCancellationService>();
        services.AddScoped<ILeaveApprovalReadService, LeaveApprovalReadService>();
        services.AddScoped<ILeaveNotificationService, LeaveNotificationService>();
        services.AddScoped<ILeaveApprovalReminderProcessor, LeaveApprovalReminderProcessor>();
        services.AddScoped<ILeaveCalendarService, LeaveCalendarService>();
        services.AddScoped<ILeaveRequestReadService, LeaveRequestReadService>();
        services.AddScoped<IHrLeaveDashboardService, HrLeaveDashboardService>();
        services.AddScoped<ILeaveReportService, LeaveReportService>();
        services.AddScoped<ILeaveRequestSubmissionRetryPolicy, LeaveRequestSubmissionRetryPolicy>();
        services.AddScoped<IImportBatchService, ImportBatchService>();
        services.AddScoped<IMasterLookupService, MasterLookupService>();
        services.AddScoped<IMasterManagementService, MasterManagementService>();
        services.AddScoped<IMasterImportService, MasterImportService>();
        services.AddScoped<IAttendanceFoundationService, AttendanceFoundationService>();
        services.AddScoped<IAttendanceBusinessDateResolver, AttendanceBusinessDateResolver>();
        services.AddScoped<IAttendanceBusinessTimeZoneProvider, AttendanceBusinessTimeZoneProvider>();
        services.AddScoped<IAttendancePunchIngestionService, AttendancePunchIngestionService>();
        services.AddScoped<IAttendanceDayProcessor, AttendanceDayProcessor>();
        services.AddScoped<IAttendanceReadService, AttendanceReadService>();
        services.AddScoped<IAttendanceWorkflowService, AttendanceWorkflowService>();
        services.AddScoped<IAttendanceMonthlyProcessor, AttendanceMonthlyProcessor>();
        services.AddScoped<IAttendancePeriodLockService, AttendancePeriodLockService>();
        services.AddScoped<IAttendanceAdminCorrectionService, AttendanceAdminCorrectionService>();
        services.AddScoped<IAttendanceReportService, AttendanceReportService>();
        services.AddScoped<ISalaryComponentService, SalaryComponentService>();
        services.AddScoped<ISalaryStructureService, SalaryStructureService>();
        services.AddScoped<IEmployeeSalaryAssignmentService, EmployeeSalaryAssignmentService>();
        services.AddScoped<IPayrollPeriodService, PayrollPeriodService>();
        services.AddScoped<IPayrollReadinessService, PayrollReadinessService>();
        services.AddScoped<IPayrollControlService, PayrollControlService>();
        services.AddScoped<IPayrollOperationsService, PayrollOperationsService>();
        services.AddScoped<IPayrollApprovalGuard, PayrollApprovalGuard>();
        services.AddScoped<IPayrollRunService, PayrollRunService>();
        services.AddScoped<IPayrollCalculationEngine, PayrollCalculationEngine>();
        services.AddScoped<IPayrollCalculationService, PayrollCalculationService>();
        services.AddScoped<IStatutoryPayrollService, StatutoryPayrollService>();
        services.AddScoped<IPayrollOutputService, PayrollOutputService>();
        services.AddScoped<IBankAdviceService, BankAdviceService>();
        services.AddScoped<IPayrollAccountingService, PayrollAccountingService>();
        services.AddScoped<IPayrollRetroSettlementService, PayrollRetroSettlementService>();
        services.AddScoped<IPayrollStatutoryComplianceService, PayrollStatutoryComplianceService>();
        services.AddScoped<IPayrollLoanService, PayrollLoanService>();
        services.AddScoped<ILoanPayrollRecoveryResolver, LoanPayrollRecoveryResolver>();
        services.AddScoped<IReimbursementService, ReimbursementService>();
        services.AddScoped<IReimbursementPayrollResolver, ReimbursementPayrollResolver>();
        services.AddScoped<IReimbursementPolicyResolver, ReimbursementPolicyResolver>();
        services.AddScoped<IReimbursementEligibilityService, ReimbursementEligibilityService>();
        services.AddScoped<ISeparationBenefitsService, SeparationBenefitsService>();
        services.AddScoped<IVariablePayService, VariablePayService>();

        return services;
    }

    private static void TryAddSingletonTimeProvider(this IServiceCollection services)
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
