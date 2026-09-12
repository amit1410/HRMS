using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class AttendanceAdminCorrectionConfiguration : IEntityTypeConfiguration<AttendanceAdminCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceAdminCorrection> b)
    {
        b.ToTable("AttendanceAdminCorrections");
        b.HasKey(x => x.Id);
        b.Property(x => x.TenantId).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.Property(x => x.Reason).HasMaxLength(2000).IsRequired();
        b.Property(x => x.CorrectionVersion).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.EmployeeId, x.BusinessDate, x.CorrectionVersion }).IsUnique();
        b.HasOne<Employee>().WithMany().HasForeignKey(x => new { x.TenantId, x.EmployeeId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<User>().WithMany().HasForeignKey(x => new { x.TenantId, x.CreatedByUserId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
