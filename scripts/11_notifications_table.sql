/* ============================================================
   MuktoAin — Notifications table (2026-09-12)
   IDEMPOTENT: safe to re-run; the CREATE is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[dbo].[NOTIFICATION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[NOTIFICATION] (
        NotificationId        INT IDENTITY(1,1) NOT NULL,
        UserId                INT                NOT NULL,
        Type                  INT                NOT NULL,
        RelatedCaseId         INT                NULL,
        RelatedDocumentId     INT                NULL,
        RelatedLawyerProfileId INT               NULL,
        IsRead                BIT                NOT NULL DEFAULT (0),
        CreatedAt             DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_NOTIFICATION PRIMARY KEY (NotificationId),
        CONSTRAINT FK_NOTIFICATION_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId) ON DELETE CASCADE,
        CONSTRAINT FK_NOTIFICATION_Case FOREIGN KEY (RelatedCaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT FK_NOTIFICATION_Document FOREIGN KEY (RelatedDocumentId)
            REFERENCES [dbo].[GENERATED_DOCUMENT] (DocumentId),
        CONSTRAINT FK_NOTIFICATION_LawyerProfile FOREIGN KEY (RelatedLawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );
    CREATE INDEX IX_NOTIFICATION_User_IsRead ON [dbo].[NOTIFICATION] (UserId, IsRead);
    CREATE INDEX IX_NOTIFICATION_CreatedAt ON [dbo].[NOTIFICATION] (CreatedAt);
END
GO
