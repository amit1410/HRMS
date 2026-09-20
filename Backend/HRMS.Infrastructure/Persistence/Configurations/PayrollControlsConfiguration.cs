using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class PayrollControlConfigurationMap : IEntityTypeConfiguration<PayrollControlConfiguration>
{
    public void Configure(EntityTypeBuilder<PayrollControlConfiguration> b)
    {
        b.ToTable("PayrollControlConfigurations");
        b.HasKey(x => x.Id);
        b.HasIndex(x => x.TenantId).IsUnique();
        b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.UpdatedAtUtc).IsRequired();
    }
}
