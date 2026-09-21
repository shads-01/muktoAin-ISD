/* Notification "seen" flag (separate from IsRead)
   - IsSeen: set when the user opens the bell dropdown; drives the badge count only.
   - IsRead: still set only when the user opens the notification itself, so
     My Cases' unread-activity dot is not cleared just by opening the bell.
   Existing read rows are backfilled as seen.
   Safe to re-run in SSMS (adds the column only when missing). */
SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns c
    WHERE c.object_id = OBJECT_ID(N'[dbo].[NOTIFICATION]')
      AND c.name = N'IsSeen'
)
BEGIN
    ALTER TABLE [dbo].[NOTIFICATION]
        ADD IsSeen BIT NOT NULL
        CONSTRAINT DF_NOTIFICATION_IsSeen DEFAULT (0);
END
GO

UPDATE [dbo].[NOTIFICATION] SET IsSeen = 1 WHERE IsRead = 1 AND IsSeen = 0;
GO
