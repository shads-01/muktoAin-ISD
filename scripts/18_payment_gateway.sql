-- scripts/18_payment_gateway.sql
-- Payment gateway rework (SSL-4, AUD-4 for PAYMENT_ORDER):
--   * TransactionId: our tran_id, sent to the gateway at session init and
--     matched against the gateway's validation response before Paid.
--   * RowVersion: optimistic concurrency, so two racing confirmations of one
--     order cannot both mark it Paid.
--   * Filtered unique index on TransactionId (NULL for orders that never
--     reached the gateway).
-- Idempotent: safe to re-run.
USE [MuktoAin];
GO
SET NOCOUNT ON;
GO
-- Required for filtered indexes.
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]')
                 AND name = N'TransactionId')
BEGIN
    ALTER TABLE [dbo].[PAYMENT_ORDER] ADD [TransactionId] NVARCHAR(100) NULL;
    PRINT 'PAYMENT_ORDER.TransactionId added.';
END
ELSE
    PRINT 'PAYMENT_ORDER.TransactionId already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]')
                 AND name = N'RowVersion')
BEGIN
    ALTER TABLE [dbo].[PAYMENT_ORDER] ADD [RowVersion] ROWVERSION;
    PRINT 'PAYMENT_ORDER.RowVersion added.';
END
ELSE
    PRINT 'PAYMENT_ORDER.RowVersion already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE name = N'UQ_PAYMENT_ORDER_TransactionId'
                 AND object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]'))
BEGIN
    CREATE UNIQUE INDEX UQ_PAYMENT_ORDER_TransactionId
        ON [dbo].[PAYMENT_ORDER] (TransactionId)
        WHERE TransactionId IS NOT NULL;
    PRINT 'UQ_PAYMENT_ORDER_TransactionId created.';
END
ELSE
    PRINT 'UQ_PAYMENT_ORDER_TransactionId already exists.';
GO
