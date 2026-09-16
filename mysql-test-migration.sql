START TRANSACTION;
ALTER TABLE `EmployeeRosterChangeHistories` ADD `ChangeType` int NOT NULL DEFAULT 0;

ALTER TABLE `EmployeeRosterChangeHistories` ADD `ChangedAtUtc` datetime(6) NOT NULL DEFAULT '0001-01-01 00:00:00.000000';

ALTER TABLE `EmployeeRosterChangeHistories` ADD `ChangedByUserId` char(36) NULL;

ALTER TABLE `EmployeeRosterChangeHistories` ADD `NewIsCalendarOverride` tinyint(1) NOT NULL DEFAULT FALSE;

ALTER TABLE `EmployeeRosterChangeHistories` ADD `OriginalCalendarDayType` int NOT NULL DEFAULT 0;

ALTER TABLE `EmployeeRosterChangeHistories` ADD `PreviousIsCalendarOverride` tinyint(1) NOT NULL DEFAULT FALSE;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260911154806_AddAttendanceRosterHistoryMetadata', '10.0.11');

COMMIT;

