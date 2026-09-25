using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class CompOffPolicyConfiguration : IEntityTypeConfiguration<CompOffPolicy>
{
    public void Configure(EntityTypeBuilder<CompOffPolicy> b)
    {
        b.ToTable("CompOffPolicies"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.Code).HasMaxLength(50).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.EligibilityMode).HasMaxLength(50).IsRequired(); b.Property(x => x.CreditRatio).HasPrecision(10, 6);
        b.Property(x => x.RoundingMode).HasConversion<int>(); b.Property(x => x.BenefitMode).HasConversion<int>();
        b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.EffectiveFrom, x.EffectiveTo });
        b.Ignore(x => x.Tenant); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompOffEarningConfiguration : IEntityTypeConfiguration<CompOffEarning>
{
    public void Configure(EntityTypeBuilder<CompOffEarning> b)
    {
        b.ToTable("CompOffEarnings"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.SourceType).HasConversion<int>(); b.Property(x => x.Status).HasConversion<int>();
        b.Property(x => x.SourceKey).HasMaxLength(180).IsRequired(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken();
        b.HasIndex(x => new { x.TenantId, x.SourceKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Status, x.ExpiresOn });
        b.Ignore(x => x.Tenant); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Employee).WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Policy).WithMany().HasForeignKey(x => new { x.TenantId, x.PolicyId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompOffLedgerEntryConfiguration : IEntityTypeConfiguration<CompOffLedgerEntry>
{
    public void Configure(EntityTypeBuilder<CompOffLedgerEntry> b)
    {
        b.ToTable("CompOffLedgerEntries"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.EntryType).HasConversion<int>(); b.Property(x => x.SourceReference).HasMaxLength(240).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(240).IsRequired(); b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.EffectiveDate });
        b.Ignore(x => x.Tenant); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Earning).WithMany(x => x.LedgerEntries).HasForeignKey(x => new { x.TenantId, x.EarningId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CompOffLeaveAllocationConfiguration : IEntityTypeConfiguration<CompOffLeaveAllocation>
{
    public void Configure(EntityTypeBuilder<CompOffLeaveAllocation> b)
    {
        b.ToTable("CompOffLeaveAllocations"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.Status).HasMaxLength(20).IsRequired(); b.HasIndex(x => new { x.TenantId, x.LeaveRequestId, x.EarningId }).IsUnique();
        b.Ignore(x => x.Tenant); b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        // Leave owns the workflow aggregate. The allocation is deliberately a narrow cross-module
        // reference so Comp-Off can reserve inside the Leave submission transaction without creating
        // a second Leave aggregate or a migration-order cycle.
        b.Ignore(x => x.LeaveRequest);
        b.HasOne(x => x.Earning).WithMany().HasForeignKey(x => new { x.TenantId, x.EarningId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
