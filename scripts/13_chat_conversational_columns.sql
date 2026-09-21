/* Conversational chat redesign (spec: docs/superpowers/specs/2026-09-15-conversational-chat-redesign-design.md)
   - CaseFileJson: structured intake slots the model re-emits each turn; C# stores
     it opaquely, last write wins.
   - Language: detected citizen language ("bn" | "en") for the explain turn.
   Safe to re-run in SSMS (adds columns only when missing). */
SET NOCOUNT ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'[dbo].[CHAT_SESSION]')
      AND c.name = N'CaseFileJson'
)
BEGIN
    ALTER TABLE [dbo].[CHAT_SESSION] ADD CaseFileJson NVARCHAR(MAX) NULL;
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'[dbo].[CHAT_SESSION]')
      AND c.name = N'Language'
)
BEGIN
    ALTER TABLE [dbo].[CHAT_SESSION] ADD Language NVARCHAR(8) NULL;
END
GO
