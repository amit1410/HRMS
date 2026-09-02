BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172901_AddFamilyDependencyFields'
)
BEGIN
    ALTER TABLE [EmployeeFamilyMembers] ADD [IsDependent] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172901_AddFamilyDependencyFields'
)
BEGIN
    ALTER TABLE [EmployeeFamilyMembers] ADD [NomineePercentage] decimal(5,2) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172901_AddFamilyDependencyFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831172901_AddFamilyDependencyFields', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172949_LinkDocumentsToPreviousEmployment'
)
BEGIN
    ALTER TABLE [EmployeeDocuments] ADD [PreviousEmploymentId] uniqueidentifier NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172949_LinkDocumentsToPreviousEmployment'
)
BEGIN
    ALTER TABLE [EmployeePreviousEmployments] ADD CONSTRAINT [AK_EmployeePreviousEmployments_TenantId_Id] UNIQUE ([TenantId], [Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172949_LinkDocumentsToPreviousEmployment'
)
BEGIN
    CREATE INDEX [IX_EmployeeDocuments_TenantId_PreviousEmploymentId] ON [EmployeeDocuments] ([TenantId], [PreviousEmploymentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172949_LinkDocumentsToPreviousEmployment'
)
BEGIN
    ALTER TABLE [EmployeeDocuments] ADD CONSTRAINT [FK_EmployeeDocuments_EmployeePreviousEmployments_TenantId_PreviousEmploymentId] FOREIGN KEY ([TenantId], [PreviousEmploymentId]) REFERENCES [EmployeePreviousEmployments] ([TenantId], [Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260831172949_LinkDocumentsToPreviousEmployment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260831172949_LinkDocumentsToPreviousEmployment', N'10.0.11');
END;

COMMIT;
GO

