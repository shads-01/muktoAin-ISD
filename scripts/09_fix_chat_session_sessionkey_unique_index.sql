/* Fix CHAT_SESSION.SessionKey uniqueness.
  Use a filtered unique index so multiple logged-in sessions can have NULL
  SessionKey values while guest keys remain unique. Safe to re-run in SSMS. */
SET NOCOUNT ON;
GO
-- Required for filtered indexes.
SET QUOTED_IDENTIFIER ON;
GO

-- Remove the old constraint.
IF EXISTS (
    SELECT 1 FROM sys.key_constraints
    WHERE name = 'UQ_CHAT_SESSION_SessionKey'
      AND parent_object_id = OBJECT_ID(N'[dbo].[CHAT_SESSION]')
)
BEGIN
    ALTER TABLE [dbo].[CHAT_SESSION] DROP CONSTRAINT UQ_CHAT_SESSION_SessionKey;
END
GO

-- Remove an old plain index under the same name.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_CHAT_SESSION_SessionKey'
      AND object_id = OBJECT_ID(N'[dbo].[CHAT_SESSION]')
      AND has_filter = 0
)
BEGIN
    DROP INDEX UQ_CHAT_SESSION_SessionKey ON [dbo].[CHAT_SESSION];
END
GO

-- Create the filtered unique index.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = 'UQ_CHAT_SESSION_SessionKey'
      AND object_id = OBJECT_ID(N'[dbo].[CHAT_SESSION]')
)
BEGIN
    CREATE UNIQUE INDEX UQ_CHAT_SESSION_SessionKey ON [dbo].[CHAT_SESSION] (SessionKey)
        WHERE SessionKey IS NOT NULL;
END
GO
