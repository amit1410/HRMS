using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HRMS.Infrastructure.MySqlMigrations.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewRoundToEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The MySQL EF migration generator crashes while translating these four
            // AddColumn operations (ColumnDefinitionWithCharSet receives no column model
            // for this late-added field). Keep the schema operation explicit and fixed-name;
            // these identifiers are migration-owned constants, never user input.
            migrationBuilder.Sql(@"
CREATE PROCEDURE `__hrms_apply_review_round`()
BEGIN
    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewCases' AND COLUMN_NAME = 'ReviewRound'
    ) THEN
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewCases' AND COLUMN_NAME = 'ReviewRound'
              AND DATA_TYPE = 'int' AND COLUMN_DEFAULT = '1' AND IS_NULLABLE = 'NO'
        ) THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ReviewRound column on ConfirmationReviewCases has an unsupported definition.';
        END IF;
    ELSE
        ALTER TABLE `ConfirmationReviewCases` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1;
    END IF;

    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews' AND COLUMN_NAME = 'ReviewRound'
    ) THEN
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews' AND COLUMN_NAME = 'ReviewRound'
              AND DATA_TYPE = 'int' AND COLUMN_DEFAULT = '1' AND IS_NULLABLE = 'NO'
        ) THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ReviewRound column on ConfirmationReviewerReviews has an unsupported definition.';
        END IF;
    ELSE
        ALTER TABLE `ConfirmationReviewerReviews` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1;
    END IF;

    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationDecisions' AND COLUMN_NAME = 'ReviewRound'
    ) THEN
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationDecisions' AND COLUMN_NAME = 'ReviewRound'
              AND DATA_TYPE = 'int' AND COLUMN_DEFAULT = '1' AND IS_NULLABLE = 'NO'
        ) THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ReviewRound column on ConfirmationDecisions has an unsupported definition.';
        END IF;
    ELSE
        ALTER TABLE `ConfirmationDecisions` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1;
    END IF;

    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationDecisionCalculations' AND COLUMN_NAME = 'ReviewRound'
    ) THEN
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationDecisionCalculations' AND COLUMN_NAME = 'ReviewRound'
              AND DATA_TYPE = 'int' AND COLUMN_DEFAULT = '1' AND IS_NULLABLE = 'NO'
        ) THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ReviewRound column on ConfirmationDecisionCalculations has an unsupported definition.';
        END IF;
    ELSE
        ALTER TABLE `ConfirmationDecisionCalculations` ADD COLUMN `ReviewRound` int NOT NULL DEFAULT 1;
    END IF;

    IF EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews'
          AND INDEX_NAME = 'IX_ConfReviewerReviews_Tenant_Case_Round_Type_Version'
    ) THEN
        IF (
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews'
              AND INDEX_NAME = 'IX_ConfReviewerReviews_Tenant_Case_Round_Type_Version'
        ) <> 5 THEN
            SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'ReviewRound replacement index has an unsupported definition.';
        END IF;
    ELSEIF NOT EXISTS (
        SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews'
          AND INDEX_NAME IN (
              'IX_ConfReviewerReviews_Tenant_Case_Type_Version',
              'IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewerType_VersionNumber',
              'IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseI~'
          )
    ) THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Known pre-ReviewRound reviewer index was not found.';
    ELSE
        CREATE UNIQUE INDEX `IX_ConfReviewerReviews_Tenant_Case_Round_Type_Version`
            ON `ConfirmationReviewerReviews` (`TenantId`, `ConfirmationReviewCaseId`, `ReviewRound`, `ReviewerType`, `VersionNumber`);
    END IF;

    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews' AND INDEX_NAME = 'IX_ConfReviewerReviews_Tenant_Case_Type_Version') THEN
        DROP INDEX `IX_ConfReviewerReviews_Tenant_Case_Type_Version` ON `ConfirmationReviewerReviews`;
    END IF;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews' AND INDEX_NAME = 'IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewerType_VersionNumber') THEN
        DROP INDEX `IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseId_ReviewerType_VersionNumber` ON `ConfirmationReviewerReviews`;
    END IF;
    IF EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ConfirmationReviewerReviews' AND INDEX_NAME = 'IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseI~') THEN
        DROP INDEX `IX_ConfirmationReviewerReviews_TenantId_ConfirmationReviewCaseI~` ON `ConfirmationReviewerReviews`;
    END IF;
END;");
            migrationBuilder.Sql("CALL `__hrms_apply_review_round`();");
            migrationBuilder.Sql("DROP PROCEDURE `__hrms_apply_review_round`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE UNIQUE INDEX `IX_ConfReviewerReviews_Tenant_Case_Type_Version` ON `ConfirmationReviewerReviews` (`TenantId`, `ConfirmationReviewCaseId`, `ReviewerType`, `VersionNumber`);");
            migrationBuilder.Sql("DROP INDEX `IX_ConfReviewerReviews_Tenant_Case_Round_Type_Version` ON `ConfirmationReviewerReviews`;");
            migrationBuilder.Sql("ALTER TABLE `ConfirmationReviewerReviews` DROP COLUMN `ReviewRound`;");
            migrationBuilder.Sql("ALTER TABLE `ConfirmationReviewCases` DROP COLUMN `ReviewRound`;");
            migrationBuilder.Sql("ALTER TABLE `ConfirmationDecisions` DROP COLUMN `ReviewRound`;");
            migrationBuilder.Sql("ALTER TABLE `ConfirmationDecisionCalculations` DROP COLUMN `ReviewRound`;");
        }

    }
}
