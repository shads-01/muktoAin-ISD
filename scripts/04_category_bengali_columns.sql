-- MuktoAin — Add Bengali columns to CASE_CATEGORY
-- Run in SSMS, with database context = MuktoAin.
--
-- 02_schema.sql's CREATE TABLE already covers these columns for a fresh
-- install (IF NOT EXISTS guards the whole table, so it won't touch a database
-- that already has CASE_CATEGORY). This script brings an already-existing
-- CASE_CATEGORY up to date with the same two columns, idempotently.

USE MuktoAin;
GO

IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[CASE_CATEGORY]') AND name = 'NameBn'
)
BEGIN
    ALTER TABLE [dbo].[CASE_CATEGORY] ADD NameBn NVARCHAR(100) NOT NULL DEFAULT N'';
END
GO

IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[CASE_CATEGORY]') AND name = 'DescriptionBn'
)
BEGIN
    ALTER TABLE [dbo].[CASE_CATEGORY] ADD DescriptionBn NVARCHAR(500) NOT NULL DEFAULT N'';
END
GO

-- Values for the 4 existing rows are backfilled by SeedCategories.cs at the
-- next app startup (it detects blank NameBn on already-seeded rows and fills
-- it from data/categories.json), not here -- this script only shapes the schema.
