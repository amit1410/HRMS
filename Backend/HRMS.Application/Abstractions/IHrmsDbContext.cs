using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

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
    bool IsMySql { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<AccountEmployeeCurrentLink> AccountEmployeeCurrentLinks { get; }
    DbSet<AccountEmployeeLinkEvent> AccountEmployeeLinkEvents { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<UserRoleAssignmentEvent> UserRoleAssignmentEvents { get; }
    DbSet<UserRoleAssignmentScope> UserRoleAssignmentScopes { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<AuthorizationConfigurationEvent> AuthorizationConfigurationEvents { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<UserInvitation> UserInvitations { get; }
    DbSet<PasswordResetOtp> PasswordResetOtps { get; }
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
    DbSet<TaxDeclarationCycle> TaxDeclarationCycles { get; }
    DbSet<TaxDeclarationCategory> TaxDeclarationCategories { get; }
    DbSet<TaxDeclarationItem> TaxDeclarationItems { get; }
    DbSet<EmployeeTaxDeclaration> EmployeeTaxDeclarations { get; }
    DbSet<EmployeeTaxDeclarationLine> EmployeeTaxDeclarationLines { get; }
    DbSet<TaxDeclarationProof> TaxDeclarationProofs { get; }
    DbSet<TaxDeclarationAuditEvent> TaxDeclarationAuditEvents { get; }
    DbSet<EmployeeEmployment> EmployeeEmployments { get; }
    DbSet<ImportBatch> ImportBatches { get; }
    DbSet<LeaveType> LeaveTypes { get; }
    DbSet<LeavePeriod> LeavePeriods { get; }
    DbSet<LeavePolicy> LeavePolicies { get; }
    DbSet<LeavePolicyVersion> LeavePolicyVersions { get; }
    DbSet<LeavePolicyRule> LeavePolicyRules { get; }
    DbSet<LeavePolicyEligibilityRule> LeavePolicyEligibilityRules { get; }
    DbSet<LeavePolicyEntitlementRule> LeavePolicyEntitlementRules { get; }
    DbSet<LeavePolicyRequestRule> LeavePolicyRequestRules { get; }
    DbSet<LeavePolicyCalendarRule> LeavePolicyCalendarRules { get; }
    DbSet<LeavePolicyAttachmentRule> LeavePolicyAttachmentRules { get; }
    DbSet<LeavePolicyClubbingRule> LeavePolicyClubbingRules { get; }
    DbSet<LeavePolicyCancellationRule> LeavePolicyCancellationRules { get; }
    DbSet<LeavePolicyApplicabilitySet> LeavePolicyApplicabilitySets { get; }
    DbSet<EmployeeLeaveBalance> EmployeeLeaveBalances { get; }
    DbSet<LeaveBalanceTransaction> LeaveBalanceTransactions { get; }
    DbSet<LeaveEntitlementGrant> LeaveEntitlementGrants { get; }
    DbSet<LeaveBalanceReservationAllocation> LeaveBalanceReservationAllocations { get; }
    DbSet<LeaveAccrualOccurrence> LeaveAccrualOccurrences { get; }
    DbSet<LeavePeriodCloseOccurrence> LeavePeriodCloseOccurrences { get; }
    DbSet<LeaveRequest> LeaveRequests { get; }
    DbSet<LeaveRequestDay> LeaveRequestDays { get; }
    DbSet<LeaveRequestEvent> LeaveRequestEvents { get; }
    DbSet<LeaveReminderDelivery> LeaveReminderDeliveries { get; }
    DbSet<Holiday> Holidays { get; }
    DbSet<WeeklyOffConfiguration> WeeklyOffConfigurations { get; }
    DbSet<WeeklyOffDay> WeeklyOffDays { get; }
    DbSet<LeaveBalanceImportBatch> LeaveBalanceImportBatches { get; }
    DbSet<LeaveBalanceImportRow> LeaveBalanceImportRows { get; }
    DbSet<Shift> Shifts { get; }
    DbSet<ShiftBreak> ShiftBreaks { get; }
    DbSet<ShiftPattern> ShiftPatterns { get; }
    DbSet<ShiftPatternDay> ShiftPatternDays { get; }
    DbSet<ShiftApplicabilityRule> ShiftApplicabilityRules { get; }
    DbSet<EmployeeRosterDay> EmployeeRosterDays { get; }
    DbSet<EmployeeRosterChangeHistory> EmployeeRosterChangeHistories { get; }
    DbSet<RosterUploadBatch> RosterUploadBatches { get; }
    DbSet<RosterUploadRow> RosterUploadRows { get; }
    DbSet<AttendancePunch> AttendancePunches { get; }
    DbSet<EmployeeAttendanceDay> EmployeeAttendanceDays { get; }
    DbSet<AttendanceRegularizationRequest> AttendanceRegularizationRequests { get; }
    DbSet<AttendanceRegularizationEvent> AttendanceRegularizationEvents { get; }
    DbSet<AttendanceAdjustment> AttendanceAdjustments { get; }
    DbSet<AttendanceAdminCorrection> AttendanceAdminCorrections { get; }
    DbSet<AttendanceOnDutyRequest> AttendanceOnDutyRequests { get; }
    DbSet<AttendanceOnDutyEvent> AttendanceOnDutyEvents { get; }
    DbSet<AttendancePeriod> AttendancePeriods { get; }
    DbSet<AttendancePeriodEvent> AttendancePeriodEvents { get; }
    DbSet<EmployeeAttendanceMonthlySummary> EmployeeAttendanceMonthlySummaries { get; }
    DbSet<SalaryComponent> SalaryComponents { get; }
    DbSet<SalaryComponentHistory> SalaryComponentHistories { get; }
    DbSet<SalaryStructure> SalaryStructures { get; }
    DbSet<SalaryStructureVersion> SalaryStructureVersions { get; }
    DbSet<SalaryStructureComponent> SalaryStructureComponents { get; }
    DbSet<SalaryStructureHistory> SalaryStructureHistories { get; }
    DbSet<EmployeeSalaryAssignment> EmployeeSalaryAssignments { get; }
    DbSet<EmployeeSalaryComponent> EmployeeSalaryComponents { get; }
    DbSet<EmployeeSalaryAssignmentHistory> EmployeeSalaryAssignmentHistories { get; }
    DbSet<PayrollPeriod> PayrollPeriods { get; }
    DbSet<PayrollControlConfiguration> PayrollControlConfigurations { get; }
    DbSet<PayrollPeriodHistory> PayrollPeriodHistories { get; }
    DbSet<PayrollRun> PayrollRuns { get; }
    DbSet<PayrollRunEmployee> PayrollRunEmployees { get; }
    DbSet<PayrollRunHistory> PayrollRunHistories { get; }
    DbSet<PayrollResult> PayrollResults { get; }
    DbSet<PayrollResultComponent> PayrollResultComponents { get; }
    DbSet<PayrollCalculationError> PayrollCalculationErrors { get; }
    DbSet<PayrollCalculationHistory> PayrollCalculationHistories { get; }
    DbSet<StatutoryConfiguration> StatutoryConfigurations { get; }
    DbSet<StatutoryConfigurationVersion> StatutoryConfigurationVersions { get; }
    DbSet<StatutoryComponentBasis> StatutoryComponentBasis { get; }
    DbSet<StatutorySlab> StatutorySlabs { get; }
    DbSet<EmployeeStatutoryProfile> EmployeeStatutoryProfiles { get; }
    DbSet<EmployeeStatutoryProfileHistory> EmployeeStatutoryProfileHistories { get; }
    DbSet<StatutoryConfigurationHistory> StatutoryConfigurationHistories { get; }
    DbSet<PayrollStatutoryResult> PayrollStatutoryResults { get; }
    DbSet<PayrollCompliancePeriod> PayrollCompliancePeriods { get; }
    DbSet<PayrollStatutoryReturnBatch> PayrollStatutoryReturnBatches { get; }
    DbSet<PayrollStatutoryReturnEmployee> PayrollStatutoryReturnEmployees { get; }
    DbSet<PayrollStatutoryReturnSource> PayrollStatutoryReturnSources { get; }
    DbSet<PayrollStatutoryComplianceHistory> PayrollStatutoryComplianceHistories { get; }
    DbSet<PayrollStatutoryChallan> PayrollStatutoryChallans { get; }
    DbSet<Payslip> Payslips { get; }
    DbSet<PayslipLine> PayslipLines { get; }
    DbSet<PayslipHistory> PayslipHistories { get; }
    DbSet<BankAdviceBatch> BankAdviceBatches { get; }
    DbSet<BankAdvicePayment> BankAdvicePayments { get; }
    DbSet<BankAdviceHistory> BankAdviceHistories { get; }
    DbSet<PayrollGLAccount> PayrollGLAccounts { get; }
    DbSet<PayrollAccountingConfiguration> PayrollAccountingConfigurations { get; }
    DbSet<PayrollAccountingConfigurationVersion> PayrollAccountingConfigurationVersions { get; }
    DbSet<PayrollGLMapping> PayrollGLMappings { get; }
    DbSet<PayrollJournalBatch> PayrollJournalBatches { get; }
    DbSet<PayrollJournalLine> PayrollJournalLines { get; }
    DbSet<PayrollJournalLineSource> PayrollJournalLineSources { get; }
    DbSet<PayrollJournalHistory> PayrollJournalHistories { get; }
    DbSet<PayrollRetroCase> PayrollRetroCases { get; }
    DbSet<PayrollRetroResult> PayrollRetroResults { get; }
    DbSet<PayrollRetroComponent> PayrollRetroComponents { get; }
    DbSet<PayrollAdjustment> PayrollAdjustments { get; }
    DbSet<PayrollAdjustmentReason> PayrollAdjustmentReasons { get; }
    DbSet<PayrollAdjustmentApplication> PayrollAdjustmentApplications { get; }
    DbSet<PayrollCorrectionSnapshot> PayrollCorrectionSnapshots { get; }
    DbSet<PayrollReversal> PayrollReversals { get; }
    DbSet<PayrollAdjustmentHistory> PayrollAdjustmentHistories { get; }
    DbSet<PayrollAdjustmentNumberSequence> PayrollAdjustmentNumberSequences { get; }
    DbSet<PayrollInputBatch> PayrollInputBatches { get; }
    DbSet<PayrollInputLine> PayrollInputLines { get; }
    DbSet<PayrollInputTemplate> PayrollInputTemplates { get; }
    DbSet<PayrollInputTemplateColumn> PayrollInputTemplateColumns { get; }
    DbSet<PayrollInputValidationIssue> PayrollInputValidationIssues { get; }
    DbSet<PayrollInputHistory> PayrollInputHistories { get; }
    DbSet<PayrollRetroHistory> PayrollRetroHistories { get; }
    DbSet<FinalSettlementCase> FinalSettlementCases { get; }
    DbSet<FinalSettlementLine> FinalSettlementLines { get; }
    DbSet<FinalSettlementHistory> FinalSettlementHistories { get; }
    DbSet<LoanProduct> LoanProducts { get; }
    DbSet<LoanProductVersion> LoanProductVersions { get; }
    DbSet<EmployeeLoan> EmployeeLoans { get; }
    DbSet<LoanInstallment> LoanInstallments { get; }
    DbSet<LoanRepayment> LoanRepayments { get; }
    DbSet<LoanHistory> LoanHistories { get; }
    DbSet<ReimbursementCategory> ReimbursementCategories { get; }
    DbSet<ReimbursementPolicyVersion> ReimbursementPolicyVersions { get; }
    DbSet<ReimbursementClaim> ReimbursementClaims { get; }
    DbSet<ReimbursementClaimLine> ReimbursementClaimLines { get; }
    DbSet<ReimbursementAttachment> ReimbursementAttachments { get; }
    DbSet<ReimbursementSettlement> ReimbursementSettlements { get; }
    DbSet<ReimbursementHistory> ReimbursementHistories { get; }
    DbSet<GratuityPolicy> GratuityPolicies { get; }
    DbSet<GratuityPolicyVersion> GratuityPolicyVersions { get; }
    DbSet<GratuityCalculation> GratuityCalculations { get; }
    DbSet<GratuityOverride> GratuityOverrides { get; }
    DbSet<LeaveEncashmentCalculation> LeaveEncashmentCalculations { get; }
    DbSet<NoticeSettlementCalculation> NoticeSettlementCalculations { get; }
    DbSet<SeparationBenefitHistory> SeparationBenefitHistories { get; }
    DbSet<VariablePayPlan> VariablePayPlans { get; }
    DbSet<VariablePayPlanVersion> VariablePayPlanVersions { get; }
    DbSet<VariablePayAward> VariablePayAwards { get; }
    DbSet<VariablePaySettlement> VariablePaySettlements { get; }
    DbSet<VariablePayAwardHistory> VariablePayAwardHistories { get; }
    DbSet<VariablePayNumberSequence> VariablePayNumberSequences { get; }
    DbSet<PayrollVarianceControl> PayrollVarianceControls { get; }
    DbSet<PayrollAnalyticsSnapshot> PayrollAnalyticsSnapshots { get; }
    DbSet<PayrollReconciliation> PayrollReconciliations { get; }
    DbSet<PayrollReconciliationFinding> PayrollReconciliationFindings { get; }
    DbSet<PayrollAnomalyFlag> PayrollAnomalyFlags { get; }
    DbSet<YearEndTaxRun> YearEndTaxRuns { get; }
    DbSet<YearEndTaxEmployee> YearEndTaxEmployees { get; }
    DbSet<YearEndTaxPreviousEmployerInput> YearEndTaxPreviousEmployerInputs { get; }
    DbSet<YearEndTaxAdjustment> YearEndTaxAdjustments { get; }
    DbSet<YearEndTaxStatement> YearEndTaxStatements { get; }
    DbSet<YearEndTaxHistory> YearEndTaxHistories { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task<IDbContextTransaction> BeginTransactionAsync(IsolationLevel isolationLevel, CancellationToken cancellationToken = default);
    IDbContextTransaction? CurrentTransaction { get; }
    void ClearChangeTracker();
}
