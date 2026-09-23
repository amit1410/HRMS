using HRMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HRMS.Infrastructure.Persistence.Configurations;

public sealed class StatutoryFilingDefinitionConfiguration : IEntityTypeConfiguration<StatutoryFilingDefinition>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingDefinition> b)
    {
        b.ToTable("StatutoryFilingDefinitions"); b.HasKey(x => x.Id); b.Property(x => x.Code).HasMaxLength(80).IsRequired(); b.Property(x => x.Name).HasMaxLength(200).IsRequired(); b.Property(x => x.FilingType).HasMaxLength(100).IsRequired(); b.Property(x => x.JurisdictionCode).HasMaxLength(20); b.Property(x => x.DestinationType).HasConversion<int>(); b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique(); b.HasOne(x => x.ConnectionProfile).WithMany(x => x.Definitions).HasForeignKey(new[] { "TenantId", "ConnectionProfileId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingConnectionProfileConfiguration : IEntityTypeConfiguration<StatutoryFilingConnectionProfile>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingConnectionProfile> b)
    {
        b.ToTable("StatutoryFilingConnectionProfiles"); b.HasKey(x => x.Id); b.Property(x => x.Name).HasMaxLength(160).IsRequired(); b.Property(x => x.ConnectorType).HasConversion<int>(); b.Property(x => x.Endpoint).HasMaxLength(500); b.Property(x => x.NonSecretConfigurationJson).HasMaxLength(4000); b.Property(x => x.SecretReference).HasMaxLength(200); b.Property(x => x.LastValidationStatus).HasMaxLength(100); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique(); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingDefinitionVersionConfiguration : IEntityTypeConfiguration<StatutoryFilingDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingDefinitionVersion> b)
    {
        b.ToTable("StatutoryFilingDefinitionVersions"); b.HasKey(x => x.Id); b.Property(x => x.OutputFormat).HasConversion<int>(); b.Property(x => x.SourceType).HasMaxLength(100); b.HasIndex(x => new { x.TenantId, x.DefinitionId, x.Version }).IsUnique(); b.HasOne(x => x.Definition).WithMany(x => x.Versions).HasForeignKey(new[] { "TenantId", "DefinitionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingFieldMappingConfiguration : IEntityTypeConfiguration<StatutoryFilingFieldMapping>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingFieldMapping> b)
    {
        b.ToTable("StatutoryFilingFieldMappings"); b.HasKey(x => x.Id); b.Property(x => x.OutputFieldName).HasMaxLength(100).IsRequired(); b.Property(x => x.SourceField).HasMaxLength(100).IsRequired(); b.Property(x => x.Format).HasMaxLength(100); b.Property(x => x.DefaultValue).HasMaxLength(500); b.HasIndex(x => new { x.TenantId, x.DefinitionVersionId, x.Sequence }).IsUnique(); b.HasOne(x => x.DefinitionVersion).WithMany(x => x.FieldMappings).HasForeignKey(new[] { "TenantId", "DefinitionVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingRunConfiguration : IEntityTypeConfiguration<StatutoryFilingRun>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingRun> b)
    {
        b.ToTable("StatutoryFilingRuns"); b.HasKey(x => x.Id); b.Property(x => x.FilingPeriod).HasMaxLength(50).IsRequired(); b.Property(x => x.Status).HasConversion<int>(); b.Property(x => x.ConcurrencyVersion).IsConcurrencyToken(); b.HasIndex(x => new { x.TenantId, x.DefinitionId, x.FilingPeriod, x.Status }); b.HasOne(x => x.Definition).WithMany().HasForeignKey(new[] { "TenantId", "DefinitionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.DefinitionVersion).WithMany().HasForeignKey(new[] { "TenantId", "DefinitionVersionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingRunItemConfiguration : IEntityTypeConfiguration<StatutoryFilingRunItem>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingRunItem> b)
    {
        b.ToTable("StatutoryFilingRunItems"); b.HasKey(x => x.Id); b.Property(x => x.SourceReference).HasMaxLength(200).IsRequired(); b.Property(x => x.SourceSnapshotJson).HasMaxLength(12000).IsRequired(); b.Property(x => x.Amount).HasPrecision(18, 2); b.HasIndex(x => new { x.TenantId, x.RunId, x.SourceReference }).IsUnique(); b.HasOne(x => x.Run).WithMany(x => x.Items).HasForeignKey(new[] { "TenantId", "RunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingValidationIssueConfiguration : IEntityTypeConfiguration<StatutoryFilingValidationIssue>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingValidationIssue> b)
    {
        b.ToTable("StatutoryFilingValidationIssues"); b.HasKey(x => x.Id); b.Property(x => x.Severity).HasConversion<int>(); b.Property(x => x.Code).HasMaxLength(100).IsRequired(); b.Property(x => x.Message).HasMaxLength(2000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.RunId, x.Code }); b.HasOne(x => x.Run).WithMany(x => x.ValidationIssues).HasForeignKey(new[] { "TenantId", "RunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.RunItem).WithMany().HasForeignKey(new[] { "TenantId", "RunItemId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingPackageConfiguration : IEntityTypeConfiguration<StatutoryFilingPackage>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingPackage> b)
    {
        b.ToTable("StatutoryFilingPackages"); b.HasKey(x => x.Id); b.Property(x => x.FileName).HasMaxLength(255).IsRequired(); b.Property(x => x.ContentType).HasMaxLength(150).IsRequired(); b.Property(x => x.PackageHash).HasMaxLength(64).IsRequired(); b.Property(x => x.Content).HasMaxLength(2000000).IsRequired(); b.HasIndex(x => new { x.TenantId, x.RunId, x.Version }).IsUnique(); b.HasIndex(x => new { x.TenantId, x.RunId, x.PackageHash }).IsUnique(); b.HasOne(x => x.PreviousPackage).WithMany().HasForeignKey(new[] { "TenantId", "PreviousPackageId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction); b.HasOne(x => x.ResubmissionOfSubmission).WithMany().HasForeignKey(new[] { "TenantId", "ResubmissionOfSubmissionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction); b.HasOne(x => x.Run).WithMany(x => x.Packages).HasForeignKey(new[] { "TenantId", "RunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingSubmissionConfiguration : IEntityTypeConfiguration<StatutoryFilingSubmission>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingSubmission> b)
    {
        b.ToTable("StatutoryFilingSubmissions"); b.HasKey(x => x.Id); b.Property(x => x.Outcome).HasConversion<int>(); b.Property(x => x.ExternalReference).HasMaxLength(200); b.Property(x => x.ResponseCode).HasMaxLength(100); b.Property(x => x.SafeResponseSummary).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.PackageId }).IsUnique(); b.HasOne(x => x.ResubmissionOfSubmission).WithMany().HasForeignKey(new[] { "TenantId", "ResubmissionOfSubmissionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.NoAction); b.HasOne(x => x.Run).WithMany(x => x.Submissions).HasForeignKey(new[] { "TenantId", "RunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Package).WithMany(x => x.Submissions).HasForeignKey(new[] { "TenantId", "PackageId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingSubmissionAttemptConfiguration : IEntityTypeConfiguration<StatutoryFilingSubmissionAttempt>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingSubmissionAttempt> b)
    {
        b.ToTable("StatutoryFilingSubmissionAttempts"); b.HasKey(x => x.Id); b.Property(x => x.Outcome).HasConversion<int>(); b.Property(x => x.ExternalReference).HasMaxLength(200); b.Property(x => x.ResponseCode).HasMaxLength(100); b.Property(x => x.SafeResponseSummary).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.SubmissionId, x.AttemptNumber }).IsUnique(); b.HasOne(x => x.Submission).WithMany(x => x.Attempts).HasForeignKey(new[] { "TenantId", "SubmissionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingAcknowledgementConfiguration : IEntityTypeConfiguration<StatutoryFilingAcknowledgement>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingAcknowledgement> b)
    {
        b.ToTable("StatutoryFilingAcknowledgements"); b.HasKey(x => x.Id); b.Property(x => x.ReferenceNumber).HasMaxLength(200).IsRequired(); b.Property(x => x.Notes).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.SubmissionId, x.ReferenceNumber }).IsUnique(); b.HasOne(x => x.Submission).WithMany(x => x.Acknowledgements).HasForeignKey(new[] { "TenantId", "SubmissionId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StatutoryFilingHistoryConfiguration : IEntityTypeConfiguration<StatutoryFilingHistory>
{
    public void Configure(EntityTypeBuilder<StatutoryFilingHistory> b)
    {
        b.ToTable("StatutoryFilingHistories"); b.HasKey(x => x.Id); b.Property(x => x.Event).HasConversion<int>(); b.Property(x => x.PreviousStatus).HasConversion<int>(); b.Property(x => x.NewStatus).HasConversion<int>(); b.Property(x => x.Message).HasMaxLength(2000); b.HasIndex(x => new { x.TenantId, x.RunId, x.OccurredAtUtc }); b.HasOne(x => x.Run).WithMany(x => x.History).HasForeignKey(new[] { "TenantId", "RunId" }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
