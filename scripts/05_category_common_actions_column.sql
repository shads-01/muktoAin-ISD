-- MuktoAin — Add CommonActions column to CASE_CATEGORY
-- Run in SSMS, with database context = MuktoAin.
--
-- Same situation as 04_category_bengali_columns.sql: 02_schema.sql's CREATE
-- TABLE covers this column for a fresh install, but won't touch a database
-- that already has CASE_CATEGORY. This script brings an existing table up to
-- date idempotently.

USE MuktoAin;
GO

IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[CASE_CATEGORY]') AND name = 'CommonActions'
)
BEGIN
    ALTER TABLE [dbo].[CASE_CATEGORY] ADD CommonActions NVARCHAR(MAX) NOT NULL DEFAULT N'';
END
GO

-- Values for the 4 existing rows are backfilled by SeedCategories.cs at the
-- next app startup (same backfill pattern as NameBn/DescriptionBn), not here --
-- this script only shapes the schema.
