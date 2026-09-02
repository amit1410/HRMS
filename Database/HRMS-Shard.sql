IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE TABLE [Permissions] (
        [Id] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(250) NULL,
        CONSTRAINT [PK_Permissions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] int NOT NULL,
        [Name] nvarchar(50) NOT NULL,
        [Description] nvarchar(250) NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [TenantCode] nvarchar(20) NOT NULL,
        [TenantName] nvarchar(200) NOT NULL,
        [Email] nvarchar(256) NULL,
        [Phone] nvarchar(30) NULL,
        [Address] nvarchar(500) NULL,
        [Status] int NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE TABLE [RolePermissions] (
        [RoleId] int NOT NULL,
        [PermissionId] int NOT NULL,
        CONSTRAINT [PK_RolePermissions] PRIMARY KEY ([RoleId], [PermissionId]),
        CONSTRAINT [FK_RolePermissions_Permissions_PermissionId] FOREIGN KEY ([PermissionId]) REFERENCES [Permissions] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_RolePermissions_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [PasswordHash] nvarchar(500) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [LastLoginDate] datetime2 NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Users_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] int NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Permissions_Name] ON [Permissions] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RolePermissions_PermissionId] ON [RolePermissions] ([PermissionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Name] ON [Roles] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_TenantCode] ON [Tenants] ([TenantCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_UserRoles_TenantId] ON [UserRoles] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_TenantId_Email] ON [Users] ([TenantId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821123404_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260821123404_InitialCreate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821175846_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [TokenHash] nvarchar(64) NOT NULL,
        [ExpiresAtUtc] datetime2 NOT NULL,
        [RevokedAtUtc] datetime2 NULL,
        [ReplacedByTokenHash] nvarchar(64) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821175846_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_TenantId_UserId] ON [RefreshTokens] ([TenantId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821175846_AddRefreshTokens'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821175846_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821175846_AddRefreshTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260821175846_AddRefreshTokens', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE TABLE [Departments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_Departments] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Departments_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Departments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE TABLE [Designations] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [Code] nvarchar(20) NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Description] nvarchar(500) NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_Designations] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Designations_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Designations_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE TABLE [Employees] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeCode] nvarchar(20) NOT NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [LastName] nvarchar(100) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Phone] nvarchar(30) NULL,
        [DateOfBirth] date NULL,
        [Gender] int NOT NULL,
        [DateOfJoining] date NOT NULL,
        [DateOfLeaving] date NULL,
        [Status] int NOT NULL,
        [DepartmentId] uniqueidentifier NOT NULL,
        [DesignationId] uniqueidentifier NOT NULL,
        [ReportingManagerId] uniqueidentifier NULL,
        [Address] nvarchar(500) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_Employees] PRIMARY KEY ([Id]),
        CONSTRAINT [AK_Employees_TenantId_Id] UNIQUE ([TenantId], [Id]),
        CONSTRAINT [FK_Employees_Departments_TenantId_DepartmentId] FOREIGN KEY ([TenantId], [DepartmentId]) REFERENCES [Departments] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Employees_Designations_TenantId_DesignationId] FOREIGN KEY ([TenantId], [DesignationId]) REFERENCES [Designations] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Employees_Employees_TenantId_ReportingManagerId] FOREIGN KEY ([TenantId], [ReportingManagerId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Employees_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Departments_TenantId_Code] ON [Departments] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Departments_TenantId_Name] ON [Departments] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Designations_TenantId_Code] ON [Designations] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Designations_TenantId_Name] ON [Designations] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE INDEX [IX_Employees_TenantId_DepartmentId] ON [Employees] ([TenantId], [DepartmentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE INDEX [IX_Employees_TenantId_DesignationId] ON [Employees] ([TenantId], [DesignationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Employees_TenantId_Email] ON [Employees] ([TenantId], [Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Employees_TenantId_EmployeeCode] ON [Employees] ([TenantId], [EmployeeCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    CREATE INDEX [IX_Employees_TenantId_ReportingManagerId] ON [Employees] ([TenantId], [ReportingManagerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260821215502_AddOrganizationAndEmployees'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260821215502_AddOrganizationAndEmployees', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823112945_AddTenantHostAndShardKey'
)
BEGIN
    ALTER TABLE [Tenants] ADD [Host] nvarchar(253) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823112945_AddTenantHostAndShardKey'
)
BEGIN
    ALTER TABLE [Tenants] ADD [ShardKey] nvarchar(64) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823112945_AddTenantHostAndShardKey'
)
BEGIN
    UPDATE Tenants
    SET Host = LOWER(TenantCode) + '.localhost',
        ShardKey = LOWER(TenantCode)
    WHERE Host = '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823112945_AddTenantHostAndShardKey'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Host] ON [Tenants] ([Host]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823112945_AddTenantHostAndShardKey'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_ShardKey] ON [Tenants] ([ShardKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260823112945_AddTenantHostAndShardKey'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260823112945_AddTenantHostAndShardKey', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [AadhaarNumber] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [BirthCity] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [BirthCountry] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [BirthState] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [BloodGroup] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [Caste] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [Citizenship] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [CostCenterCode] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [EmployeeType] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [EsicApplicable] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [EsicNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [Gratuity] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [GroupDateOfJoining] date NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [GroupId] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [JobStatus] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [LanguageKnown] nvarchar(500) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [MaritalStatus] int NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [MediclaimNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [MiddleName] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [PanNumber] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [PayrollLocation] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [Pension] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [PfNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [ProfilePictureUrl] nvarchar(1000) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [Religion] nvarchar(100) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [Salutation] nvarchar(20) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    ALTER TABLE [Employees] ADD [UanNumber] nvarchar(50) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeAdditionalInfo] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [Division] nvarchar(200) NULL,
        [PaPsa] nvarchar(100) NULL,
        [AdditionalEmployeeCode] nvarchar(50) NULL,
        [ContractId] nvarchar(100) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeAdditionalInfo] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeAdditionalInfo_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeAdditionalInfo_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeAddresses] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [AddressType] int NOT NULL,
        [Country] nvarchar(100) NULL,
        [State] nvarchar(100) NULL,
        [City] nvarchar(100) NULL,
        [ZipCode] nvarchar(20) NULL,
        [AddressLine1] nvarchar(500) NULL,
        [AddressLine2] nvarchar(500) NULL,
        [HouseNumber] nvarchar(50) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeAddresses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeAddresses_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeAddresses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeAuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [EmployeeCode] nvarchar(20) NULL,
        [Module] nvarchar(100) NOT NULL,
        [Section] nvarchar(100) NULL,
        [EntityName] nvarchar(200) NULL,
        [RecordId] uniqueidentifier NULL,
        [FieldName] nvarchar(200) NULL,
        [OldValue] nvarchar(2000) NULL,
        [NewValue] nvarchar(2000) NULL,
        [ChangeType] int NOT NULL,
        [EffectiveDate] date NULL,
        [ChangedBy] nvarchar(256) NOT NULL,
        [Reason] nvarchar(500) NULL,
        [Source] nvarchar(50) NULL,
        [ImportBatchId] uniqueidentifier NULL,
        [IpAddress] nvarchar(50) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeAuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeAuditLogs_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeAuditLogs_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeBankDetails] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [BankName] nvarchar(200) NOT NULL,
        [AccountHolderName] nvarchar(200) NOT NULL,
        [AccountNumber] nvarchar(50) NOT NULL,
        [AccountType] int NOT NULL,
        [AccountPurpose] int NOT NULL,
        [IfscCode] nvarchar(20) NULL,
        [DocumentOfProof] nvarchar(500) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeBankDetails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeBankDetails_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeBankDetails_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeContacts] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [OfficialEmail] nvarchar(256) NULL,
        [PersonalEmail] nvarchar(256) NULL,
        [OfficialPhone] nvarchar(30) NULL,
        [PersonalPhone] nvarchar(30) NULL,
        [EmergencyNumber] nvarchar(30) NULL,
        [SameAsCurrentAddress] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeContacts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeContacts_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeContacts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeDocuments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [DocumentName] nvarchar(200) NOT NULL,
        [DocumentCategory] int NOT NULL,
        [DocumentNumber] nvarchar(100) NULL,
        [FilePath] nvarchar(1000) NOT NULL,
        [FileSize] bigint NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [UploadedBy] nvarchar(256) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeDocuments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeDocuments_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeDocuments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeEducationRecords] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [EducationLevel] nvarchar(100) NOT NULL,
        [Qualification] nvarchar(200) NOT NULL,
        [University] nvarchar(200) NULL,
        [Institute] nvarchar(200) NULL,
        [EducationType] int NOT NULL,
        [AreaOfSpecialization] nvarchar(200) NULL,
        [YearOfPassing] int NULL,
        [Score] nvarchar(50) NULL,
        [DocumentOfProof] nvarchar(500) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeEducationRecords] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeEducationRecords_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeEducationRecords_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeEmploymentHistory] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [EffectiveFrom] date NOT NULL,
        [EffectiveTo] date NULL,
        [BusinessRole] nvarchar(200) NULL,
        [HoldingCompany] nvarchar(200) NULL,
        [LineOfBusiness] nvarchar(200) NULL,
        [Organisation] nvarchar(200) NULL,
        [Grade] nvarchar(50) NULL,
        [GradeLevel] nvarchar(50) NULL,
        [DepartmentCode] nvarchar(20) NULL,
        [DepartmentName] nvarchar(200) NULL,
        [DepartmentId] uniqueidentifier NULL,
        [SubDepartment] nvarchar(200) NULL,
        [Function] nvarchar(200) NULL,
        [SubFunction] nvarchar(200) NULL,
        [Section] nvarchar(200) NULL,
        [SubSection] nvarchar(200) NULL,
        [Location] nvarchar(200) NULL,
        [WorkLocation] nvarchar(200) NULL,
        [CareerGroup] nvarchar(100) NULL,
        [EmploymentType] int NOT NULL,
        [EmploymentStatus] int NOT NULL,
        [DesignationName] nvarchar(200) NULL,
        [DesignationId] uniqueidentifier NULL,
        [ChangeReason] int NOT NULL,
        [ChangeReasonDescription] nvarchar(500) NULL,
        [ManagerCode] nvarchar(20) NULL,
        [ManagerId] uniqueidentifier NULL,
        [ManagerName] nvarchar(200) NULL,
        [CreatedBy] nvarchar(256) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeEmploymentHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeEmploymentHistory_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeEmploymentHistory_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeFamilyMembers] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [Salutation] nvarchar(20) NULL,
        [FirstName] nvarchar(100) NOT NULL,
        [MiddleName] nvarchar(100) NULL,
        [LastName] nvarchar(100) NOT NULL,
        [Relationship] nvarchar(50) NOT NULL,
        [Gender] int NOT NULL,
        [DateOfBirth] date NULL,
        [BloodGroup] int NOT NULL,
        [Nationality] nvarchar(100) NULL,
        [Occupation] nvarchar(200) NULL,
        [IsNominee] bit NOT NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeFamilyMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeFamilyMembers_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeFamilyMembers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeePreviousEmployments] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [Company] nvarchar(200) NOT NULL,
        [Designation] nvarchar(200) NULL,
        [Location] nvarchar(200) NULL,
        [EmploymentType] int NOT NULL,
        [TenureFrom] date NULL,
        [TenureTill] date NULL,
        [DocumentOfProof] nvarchar(500) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeePreviousEmployments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeePreviousEmployments_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeePreviousEmployments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [EmployeeSupervisors] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [EmployeeId] uniqueidentifier NOT NULL,
        [L1ManagerCode] nvarchar(20) NULL,
        [L1ManagerName] nvarchar(200) NULL,
        [L1ManagerId] uniqueidentifier NULL,
        [L2ManagerCode] nvarchar(20) NULL,
        [L2ManagerName] nvarchar(200) NULL,
        [L2ManagerId] uniqueidentifier NULL,
        [L3ManagerCode] nvarchar(20) NULL,
        [L3ManagerName] nvarchar(200) NULL,
        [L3ManagerId] uniqueidentifier NULL,
        [L4ManagerCode] nvarchar(20) NULL,
        [L4ManagerName] nvarchar(200) NULL,
        [L4ManagerId] uniqueidentifier NULL,
        [L5ManagerCode] nvarchar(20) NULL,
        [L5ManagerName] nvarchar(200) NULL,
        [L5ManagerId] uniqueidentifier NULL,
        [TimeManagerCode] nvarchar(20) NULL,
        [TimeManagerName] nvarchar(200) NULL,
        [TimeManagerId] uniqueidentifier NULL,
        [EroCode] nvarchar(20) NULL,
        [EroName] nvarchar(200) NULL,
        [EroId] uniqueidentifier NULL,
        [ChroManagerCode] nvarchar(20) NULL,
        [ChroManagerName] nvarchar(200) NULL,
        [ChroManagerId] uniqueidentifier NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_EmployeeSupervisors] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmployeeSupervisors_Employees_TenantId_EmployeeId] FOREIGN KEY ([TenantId], [EmployeeId]) REFERENCES [Employees] ([TenantId], [Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_EmployeeSupervisors_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE TABLE [ImportBatches] (
        [Id] uniqueidentifier NOT NULL,
        [TenantId] uniqueidentifier NOT NULL,
        [FileName] nvarchar(500) NULL,
        [ImportedBy] nvarchar(256) NOT NULL,
        [TotalRows] int NOT NULL,
        [SuccessfulRows] int NOT NULL,
        [FailedRows] int NOT NULL,
        [SkippedRows] int NOT NULL,
        [Status] nvarchar(50) NOT NULL,
        [StartedAtUtc] datetime2 NULL,
        [CompletedAtUtc] datetime2 NULL,
        [Message] nvarchar(2000) NULL,
        [CreatedDate] datetime2 NOT NULL,
        [ModifiedDate] datetime2 NULL,
        CONSTRAINT [PK_ImportBatches] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ImportBatches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmployeeAdditionalInfo_TenantId_EmployeeId] ON [EmployeeAdditionalInfo] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmployeeAddresses_TenantId_EmployeeId_AddressType] ON [EmployeeAddresses] ([TenantId], [EmployeeId], [AddressType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeAuditLogs_TenantId_EmployeeId_CreatedDate] ON [EmployeeAuditLogs] ([TenantId], [EmployeeId], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeAuditLogs_TenantId_EmployeeId_Module] ON [EmployeeAuditLogs] ([TenantId], [EmployeeId], [Module]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeAuditLogs_TenantId_ImportBatchId] ON [EmployeeAuditLogs] ([TenantId], [ImportBatchId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeBankDetails_TenantId_EmployeeId] ON [EmployeeBankDetails] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmployeeContacts_TenantId_EmployeeId] ON [EmployeeContacts] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeDocuments_TenantId_EmployeeId] ON [EmployeeDocuments] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeEducationRecords_TenantId_EmployeeId] ON [EmployeeEducationRecords] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeEmploymentHistory_TenantId_EmployeeId_EffectiveFrom] ON [EmployeeEmploymentHistory] ([TenantId], [EmployeeId], [EffectiveFrom]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeEmploymentHistory_TenantId_EmployeeId_EffectiveTo] ON [EmployeeEmploymentHistory] ([TenantId], [EmployeeId], [EffectiveTo]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeeFamilyMembers_TenantId_EmployeeId] ON [EmployeeFamilyMembers] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_EmployeePreviousEmployments_TenantId_EmployeeId] ON [EmployeePreviousEmployments] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE UNIQUE INDEX [IX_EmployeeSupervisors_TenantId_EmployeeId] ON [EmployeeSupervisors] ([TenantId], [EmployeeId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    CREATE INDEX [IX_ImportBatches_TenantId_CreatedDate] ON [ImportBatches] ([TenantId], [CreatedDate]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260825101745_AddEmployeeEnhancements'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260825101745_AddEmployeeEnhancements', N'10.0.11');
END;

COMMIT;
GO

