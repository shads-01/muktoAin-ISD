-- scripts/17_add_rowversion_columns.sql
-- AUD-4: optimistic concurrency tokens for GENERATED_DOCUMENT and CASE.
-- (PAYMENT_ORDER left out: payment flow is being rebuilt separately.)
-- Idempotent: safe to re-run. ROWVERSION is NOT NULL and engine-maintained.
USE [MuktoAin];
GO
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]')
                 AND name = N'RowVersion')
BEGIN
    ALTER TABLE [dbo].[GENERATED_DOCUMENT] ADD [RowVersion] ROWVERSION;
    PRINT 'GENERATED_DOCUMENT.RowVersion added.';
END
ELSE
    PRINT 'GENERATED_DOCUMENT.RowVersion already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[CASE]')
                 AND name = N'RowVersion')
BEGIN
    ALTER TABLE [dbo].[CASE] ADD [RowVersion] ROWVERSION;
    PRINT 'CASE.RowVersion added.';
END
ELSE
    PRINT 'CASE.RowVersion already exists.';
GO
