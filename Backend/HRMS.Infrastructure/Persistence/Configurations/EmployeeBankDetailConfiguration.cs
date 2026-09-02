using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public class EmployeeBankDetailConfiguration : IEntityTypeConfiguration<EmployeeBankDetail>
{
    public void Configure(EntityTypeBuilder<EmployeeBankDetail> builder)
    {
        builder.ToTable("EmployeeBankDetails");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeId).IsRequired();
        builder.Property(e => e.AccountHolderName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.AccountNumber).IsRequired().HasMaxLength(50);
        builder.Property(e => e.AccountType).HasConversion<int>().IsRequired();
        builder.Property(e => e.AccountPurpose).HasConversion<int>().IsRequired();
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();
        builder.Property(e => e.IfscCode).HasMaxLength(20);
        builder.Property(e => e.BranchName).HasMaxLength(200);
        builder.Property(e => e.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(e => e.DocumentOfProof).HasMaxLength(500);

        builder.HasIndex(e => new { e.TenantId, e.EmployeeId });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Employee)
            .WithMany(e => e.BankDetails)
            .HasForeignKey(e => new { e.TenantId, e.EmployeeId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Bank)
            .WithMany(b => b.EmployeeBankDetails)
            .HasForeignKey(e => new { e.TenantId, e.BankId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
