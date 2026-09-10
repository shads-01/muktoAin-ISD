/* Widen CASE.Title for encrypted values.
  Matches Description's NVARCHAR(MAX) capacity. Safe to re-run in SSMS. */
SET NOCOUNT ON;
GO

IF EXISTS (
    SELECT 1 FROM sys.columns c
    JOIN sys.types t ON c.user_type_id = t.user_type_id
    WHERE c.object_id = OBJECT_ID(N'[dbo].[CASE]')
      AND c.name = N'Title'
      AND NOT (t.name = 'nvarchar' AND c.max_length = -1)
)
BEGIN
    ALTER TABLE [dbo].[CASE] ALTER COLUMN Title NVARCHAR(MAX) NOT NULL;
END
GO
