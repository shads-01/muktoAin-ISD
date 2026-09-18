/* ============================================================
   MuktoAin — Admin tiers: USER.IsSuperAdmin (2026-09-12)
   IDEMPOTENT: safe to re-run; the ALTER is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[USER]') AND name = N'IsSuperAdmin')
    ALTER TABLE [dbo].[USER] ADD IsSuperAdmin BIT NOT NULL DEFAULT (0);
GO
