using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollInputBatchConfiguration : IEntityTypeConfiguration<PayrollInputBatch>
{
    public void Configure(EntityTypeBuilder<PayrollInputBatch> b)
    {
        b.ToTable("PayrollInputBatches"); b.HasKey(x => x.Id);
        b.Property(x => x.BatchNumber).HasMaxLength(60).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.SourceType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.TotalAmount).HasPrecision(18, 2); b.Property(x => x.FileName).HasMaxLength(260); b.Property(x => x.FileHash).HasMaxLength(128); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.BatchNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.FileHash, x.TemplateId }).IsUnique().HasFilter("[FileHash] IS NOT NULL");
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.PayrollPeriod).WithMany().HasForeignKey(nameof(PayrollInputBatch.TenantId), nameof(PayrollInputBatch.PayrollPeriodId)).HasPrincipalKey(nameof(PayrollPeriod.TenantId), nameof(PayrollPeriod.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Template).WithMany().HasForeignKey(nameof(PayrollInputBatch.TenantId), nameof(PayrollInputBatch.TemplateId)).HasPrincipalKey(nameof(PayrollInputTemplate.TenantId), nameof(PayrollInputTemplate.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class PayrollInputLineConfiguration : IEntityTypeConfiguration<PayrollInputLine>
{
    public void Configure(EntityTypeBuilder<PayrollInputLine> b)
    {
        b.ToTable("PayrollInputLines"); b.HasKey(x => x.Id); b.Property(x => x.EmployeeCodeSnapshot).HasMaxLength(100).IsRequired(); b.Property(x => x.ComponentCodeSnapshot).HasMaxLength(100); b.Property(x => x.InputType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.Amount).HasPrecision(18, 2); b.Property(x => x.Quantity).HasPrecision(18, 4); b.Property(x => x.Rate).HasPrecision(18, 4); b.Property(x => x.SourceRowHash).HasMaxLength(128).IsRequired(); b.Property(x => x.ReferenceNumber).HasMaxLength(200); b.Property(x => x.Remarks).HasMaxLength(2000); b.Property(x => x.ValidationMessage).HasMaxLength(4000); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.BatchId, x.RowNumber }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.SourceRowHash }); b.HasOne(x => x.Batch).WithMany(x => x.Lines).HasForeignKey(nameof(PayrollInputLine.TenantId), nameof(PayrollInputLine.BatchId)).HasPrincipalKey(nameof(PayrollInputBatch.TenantId), nameof(PayrollInputBatch.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Employee).WithMany().HasForeignKey(nameof(PayrollInputLine.TenantId), nameof(PayrollInputLine.EmployeeId)).HasPrincipalKey(nameof(Employee.TenantId), nameof(Employee.Id)).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.SalaryComponent).WithMany().HasForeignKey(nameof(PayrollInputLine.TenantId), nameof(PayrollInputLine.SalaryComponentId)).HasPrincipalKey(nameof(SalaryComponent.TenantId), nameof(SalaryComponent.Id)).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class PayrollInputTemplateConfiguration : IEntityTypeConfiguration<PayrollInputTemplate>
{
    public void Configure(EntityTypeBuilder<PayrollInputTemplate> b) { b.ToTable("PayrollInputTemplates"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(60).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.Description).HasMaxLength(2000); b.Property(x => x.InputType).HasConversion<int>(); b.Property(x => x.Delimiter).HasMaxLength(1).IsRequired(); b.Property(x => x.DateFormat).HasMaxLength(50).IsRequired(); b.HasIndex(x => new { x.TenantId, x.Code, x.Version }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class PayrollInputTemplateColumnConfiguration : IEntityTypeConfiguration<PayrollInputTemplateColumn>
{
    public void Configure(EntityTypeBuilder<PayrollInputTemplateColumn> b) { b.ToTable("PayrollInputTemplateColumns"); b.HasKey(x => x.Id); b.Property(x => x.SourceColumnName).HasMaxLength(100).IsRequired(); b.Property(x => x.TargetField).HasMaxLength(100).IsRequired(); b.Property(x => x.DefaultValue).HasMaxLength(500); b.Property(x => x.TransformType).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.TemplateId, x.Position }).IsUnique(); b.HasOne(x => x.Template).WithMany(x => x.Columns).HasForeignKey(nameof(PayrollInputTemplateColumn.TenantId), nameof(PayrollInputTemplateColumn.TemplateId)).HasPrincipalKey(nameof(PayrollInputTemplate.TenantId), nameof(PayrollInputTemplate.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}
public sealed class PayrollInputValidationIssueConfiguration : IEntityTypeConfiguration<PayrollInputValidationIssue>
{
    public void Configure(EntityTypeBuilder<PayrollInputValidationIssue> b) { b.ToTable("PayrollInputValidationIssues"); b.HasKey(x => x.Id); b.Property(x => x.Severity).HasConversion<int>(); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.FieldName).HasMaxLength(100); b.Property(x => x.Message).HasMaxLength(2000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.BatchId, x.Severity, x.RowNumber }); b.HasOne(x => x.Batch).WithMany(x => x.Issues).HasForeignKey(nameof(PayrollInputValidationIssue.TenantId), nameof(PayrollInputValidationIssue.BatchId)).HasPrincipalKey(nameof(PayrollInputBatch.TenantId), nameof(PayrollInputBatch.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Line).WithMany().HasForeignKey(nameof(PayrollInputValidationIssue.TenantId), nameof(PayrollInputValidationIssue.LineId)).HasPrincipalKey(nameof(PayrollInputLine.TenantId), nameof(PayrollInputLine.Id)).OnDelete(DeleteBehavior.NoAction); }
}
public sealed class PayrollInputHistoryConfiguration : IEntityTypeConfiguration<PayrollInputHistory>
{
    public void Configure(EntityTypeBuilder<PayrollInputHistory> b) { b.ToTable("PayrollInputHistories"); b.HasKey(x => x.Id); b.Property(x => x.Event).HasConversion<int>(); b.Property(x => x.PreviousStatus).HasConversion<int>(); b.Property(x => x.NewStatus).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.BatchId, x.OccurredAtUtc }); b.HasOne(x => x.Batch).WithMany(x => x.History).HasForeignKey(nameof(PayrollInputHistory.TenantId), nameof(PayrollInputHistory.BatchId)).HasPrincipalKey(nameof(PayrollInputBatch.TenantId), nameof(PayrollInputBatch.Id)).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict); }
}
