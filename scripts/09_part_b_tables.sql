/* ============================================================
   MuktoAin — Part B schema additions (2026-09-05)
   Rejection reason column on LAWYER_PROFILE (FR-15).
   ============================================================ */
SET NOCOUNT ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[LAWYER_PROFILE]') AND name = N'RejectionReason')
    ALTER TABLE [dbo].[LAWYER_PROFILE] ADD RejectionReason NVARCHAR(500) NULL;
GO
