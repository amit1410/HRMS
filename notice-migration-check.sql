START TRANSACTION;
ALTER TABLE `EmployeeEmployments` ADD `NoticeEndDate` date NULL;

ALTER TABLE `EmployeeEmployments` ADD `NoticeStartDate` date NULL;

ALTER TABLE `EmployeeEmployments` ADD `NoticeStatus` int NOT NULL DEFAULT 0;

INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
VALUES ('20260913130001_AddEmployeeNoticePeriod', '10.0.11');

COMMIT;

