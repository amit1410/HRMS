using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

/// <summary>
/// Employee mapping. The three foreign keys deserve an explanation: each is <b>composite</b>, pairing the
/// referenced id with <c>TenantId</c> against an alternate key of <c>(TenantId, Id)</c> on the principal.
/// A plain <c>DepartmentId</c> foreign key would happily accept another tenant's department — the row
/// exists, so the constraint is satisfied — and global query filters would not catch it, because they
/// filter reads, not writes. Pairing the tenant into the key makes a cross-tenant reference violate the
/// constraint outright, so the isolation rule holds even if application code someday forgets to check.
/// </summary>
public class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.TenantId).IsRequired();
        builder.Property(e => e.EmployeeCode).IsRequired().HasMaxLength(20);
        builder.Property(e => e.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.LastName).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Email).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Phone).HasMaxLength(30);
        builder.Property(e => e.Address).HasMaxLength(500);
        builder.Property(e => e.DateOfJoining).IsRequired();
        builder.Property(e => e.Gender).HasConversion<int>().IsRequired();
        builder.Property(e => e.Status).HasConversion<int>().IsRequired();

        // Both the employee code and the work email identify a person within their organization, so both
        // are unique per tenant rather than globally.
        builder.HasIndex(e => new { e.TenantId, e.EmployeeCode }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Email }).IsUnique();

        // Supports the list filters and the "does anything still reference this?" checks made before a
        // department, designation or manager is deleted.
        builder.HasIndex(e => new { e.TenantId, e.DepartmentId });
        builder.HasIndex(e => new { e.TenantId, e.DesignationId });
        builder.HasIndex(e => new { e.TenantId, e.ReportingManagerId });

        // Referenced by (TenantId, Id) from the self-referencing manager relationship below.
        builder.HasAlternateKey(e => new { e.TenantId, e.Id });

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict, not Cascade: deleting a department must never silently delete the people in it.
        builder.HasOne(e => e.Department)
            .WithMany(d => d.Employees)
            .HasForeignKey(e => new { e.TenantId, e.DepartmentId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Designation)
            .WithMany(d => d.Employees)
            .HasForeignKey(e => new { e.TenantId, e.DesignationId })
            .HasPrincipalKey(d => new { d.TenantId, d.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Self-reference: a manager is another employee of the same tenant. ReportingManagerId is
        // nullable, which is what makes this relationship optional despite TenantId being required.
        builder.HasOne(e => e.ReportingManager)
            .WithMany(e => e.DirectReports)
            .HasForeignKey(e => new { e.TenantId, e.ReportingManagerId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
