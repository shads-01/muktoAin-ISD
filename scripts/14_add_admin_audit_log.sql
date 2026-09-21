/* ============================================================
   MuktoAin — Admin audit trail (2026-09-12, AUD-7)
   Real administrative action log: who suspended/reactivated a
   user, verified/rejected a lawyer, refunded/marked-paid an
   order, deleted a scenario mapping (docs/PROJECT_AUDIT_REPORT.md
   — Admin Scope #5).
   IDEMPOTENT: safe to re-run; CREATE is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[dbo].[ADMIN_AUDIT_LOG]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ADMIN_AUDIT_LOG] (
        AdminAuditLogId INT IDENTITY(1,1) NOT NULL,
        AdminUserId     INT               NOT NULL,
        [Action]        NVARCHAR(50)      NOT NULL,
        TargetUserId    INT               NULL,
        TargetEntityId  INT               NULL,
        Details         NVARCHAR(1000)    NULL,
        CreatedAt       DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ADMIN_AUDIT_LOG PRIMARY KEY (AdminAuditLogId),
        CONSTRAINT FK_ADMIN_AUDIT_LOG_AdminUser FOREIGN KEY (AdminUserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_ADMIN_AUDIT_LOG_TargetUser FOREIGN KEY (TargetUserId)
            REFERENCES [dbo].[USER] (UserId)
    );
    CREATE INDEX IX_ADMIN_AUDIT_LOG_CreatedAt ON [dbo].[ADMIN_AUDIT_LOG] (CreatedAt DESC);
    CREATE INDEX IX_ADMIN_AUDIT_LOG_AdminUserId ON [dbo].[ADMIN_AUDIT_LOG] (AdminUserId);
    CREATE INDEX IX_ADMIN_AUDIT_LOG_Action ON [dbo].[ADMIN_AUDIT_LOG] ([Action]);
END
GO
