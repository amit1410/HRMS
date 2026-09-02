IF OBJECT_ID(N'[__EFMigrationsHistoryCatalog]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistoryCatalog] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistoryCatalog] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistoryCatalog]
    WHERE [MigrationId] = N'20260823113202_InitialCatalog'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] uniqueidentifier NOT NULL,
        [TenantCode] nvarchar(20) NOT NULL,
        [Host] nvarchar(253) NOT NULL,
        [ShardKey] nvarchar(64) NOT NULL,
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
    SELECT * FROM [__EFMigrationsHistoryCatalog]
    WHERE [MigrationId] = N'20260823113202_InitialCatalog'
)
BEGIN
    CREATE TABLE [TenantBranding] (
        [TenantId] uniqueidentifier NOT NULL,
        [IsPublic] bit NOT NULL,
        [DisplayName] nvarchar(100) NULL,
        [LogoUrl] nvarchar(512) NULL,
        [PrimaryColor] nvarchar(7) NULL,
        [WelcomeMessage] nvarchar(160) NULL,
        [SupportEmail] nvarchar(256) NULL,
        [SsoEnabled] bit NOT NULL,
        [SsoProviderName] nvarchar(50) NULL,
        CONSTRAINT [PK_TenantBranding] PRIMARY KEY ([TenantId]),
        CONSTRAINT [FK_TenantBranding_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistoryCatalog]
    WHERE [MigrationId] = N'20260823113202_InitialCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Host] ON [Tenants] ([Host]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistoryCatalog]
    WHERE [MigrationId] = N'20260823113202_InitialCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_ShardKey] ON [Tenants] ([ShardKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistoryCatalog]
    WHERE [MigrationId] = N'20260823113202_InitialCatalog'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_TenantCode] ON [Tenants] ([TenantCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistoryCatalog]
    WHERE [MigrationId] = N'20260823113202_InitialCatalog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistoryCatalog] ([MigrationId], [ProductVersion])
    VALUES (N'20260823113202_InitialCatalog', N'10.0.11');
END;

COMMIT;
GO

