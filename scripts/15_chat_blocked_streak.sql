/* Chat blocked-streak escalation (plan: docs/superpowers/plans/2026-09-18-chat-core-polish.md)
   - BlockedStreak: consecutive safety-blocked turns; at 3, Status flips to 2 (Blocked).
   - Status column now also stores 2 = ChatSessionStatus.Blocked (no schema change needed for the enum).
   Safe to re-run in SSMS (adds the column only when missing). */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'[dbo].[CHAT_SESSION]')
      AND c.name = N'BlockedStreak'
)
BEGIN
    ALTER TABLE [dbo].[CHAT_SESSION]
        ADD BlockedStreak INT NOT NULL
        CONSTRAINT DF_CHAT_SESSION_BlockedStreak DEFAULT (0);
END
GO
