/* ============================================================
   MuktoAin — Frontend Redesign additive schema (2026-09-05)
   Chat sessions/messages, answer cache, sandbox payments,
   CASE / GENERATED_DOCUMENT extension columns.
   IDEMPOTENT: safe to re-run; every CREATE is guarded.
   Execute in SSMS against the MuktoAin database.
   ============================================================ */
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'[dbo].[CHAT_SESSION]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CHAT_SESSION] (
        ChatSessionId    INT IDENTITY(1,1) NOT NULL,
        UserId           INT                NULL,
        SessionKey       NVARCHAR(64)       NULL,
        Title            NVARCHAR(250)      NOT NULL DEFAULT (N''),
        Status           INT                NOT NULL DEFAULT (0),
        CommittedCaseId  INT                NULL,
        CreatedAt        DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        UpdatedAt        DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_CHAT_SESSION PRIMARY KEY (ChatSessionId),
        CONSTRAINT FK_CHAT_SESSION_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_CHAT_SESSION_Case FOREIGN KEY (CommittedCaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT UQ_CHAT_SESSION_SessionKey UNIQUE (SessionKey)
    );
    CREATE INDEX IX_CHAT_SESSION_UserId ON [dbo].[CHAT_SESSION] (UserId);
    CREATE INDEX IX_CHAT_SESSION_Status ON [dbo].[CHAT_SESSION] (Status);
END
GO

IF OBJECT_ID(N'[dbo].[CHAT_MESSAGE]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CHAT_MESSAGE] (
        ChatMessageId   INT IDENTITY(1,1) NOT NULL,
        ChatSessionId   INT                NOT NULL,
        Role            NVARCHAR(16)       NOT NULL DEFAULT (N'user'),
        Content         NVARCHAR(MAX)      NOT NULL,
        CitedJson       NVARCHAR(MAX)      NULL,
        CreatedAt       DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_CHAT_MESSAGE PRIMARY KEY (ChatMessageId),
        CONSTRAINT FK_CHAT_MESSAGE_Session FOREIGN KEY (ChatSessionId)
            REFERENCES [dbo].[CHAT_SESSION] (ChatSessionId) ON DELETE CASCADE
    );
    CREATE INDEX IX_CHAT_MESSAGE_Session ON [dbo].[CHAT_MESSAGE] (ChatSessionId);
END
GO

IF OBJECT_ID(N'[dbo].[ANSWER_CACHE]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ANSWER_CACHE] (
        AnswerCacheId  INT IDENTITY(1,1) NOT NULL,
        QueryHash      CHAR(64)           NOT NULL,
        Question       NVARCHAR(500)      NOT NULL,
        Answer         NVARCHAR(MAX)      NOT NULL,
        CitedJson      NVARCHAR(MAX)      NULL,
        HitCount       INT                NOT NULL DEFAULT (0),
        CreatedAt      DATETIME2          NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_ANSWER_CACHE PRIMARY KEY (AnswerCacheId),
        CONSTRAINT UQ_ANSWER_CACHE_QueryHash UNIQUE (QueryHash)
    );
END
GO

IF OBJECT_ID(N'[dbo].[PAYMENT_ORDER]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAYMENT_ORDER] (
        PaymentOrderId  INT IDENTITY(1,1) NOT NULL,
        UserId          INT                NULL,
        CaseId          INT                NULL,
        LawyerProfileId INT                NULL,
        Purpose         INT                NOT NULL DEFAULT (0),
        Status          INT                NOT NULL DEFAULT (0),
        Amount          DECIMAL(10,2)     NOT NULL DEFAULT (0),
        Commission      DECIMAL(10,2)     NOT NULL DEFAULT (0),
        NetToLawyer     DECIMAL(10,2)     NOT NULL DEFAULT (0),
        GatewayRef      NVARCHAR(200)     NULL,
        CreatedAt       DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),
        PaidAt          DATETIME2         NULL,
        RefundedAt      DATETIME2         NULL,
        CONSTRAINT PK_PAYMENT_ORDER PRIMARY KEY (PaymentOrderId),
        CONSTRAINT FK_PAYMENT_ORDER_User FOREIGN KEY (UserId)
            REFERENCES [dbo].[USER] (UserId),
        CONSTRAINT FK_PAYMENT_ORDER_Case FOREIGN KEY (CaseId)
            REFERENCES [dbo].[CASE] (CaseId),
        CONSTRAINT FK_PAYMENT_ORDER_LawyerProfile FOREIGN KEY (LawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );
    CREATE INDEX IX_PAYMENT_ORDER_Status ON [dbo].[PAYMENT_ORDER] (Status);
    CREATE INDEX IX_PAYMENT_ORDER_LawyerProfileId ON [dbo].[PAYMENT_ORDER] (LawyerProfileId);
END
GO

IF OBJECT_ID(N'[dbo].[PAYOUT_REQUEST]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PAYOUT_REQUEST] (
        PayoutRequestId INT IDENTITY(1,1) NOT NULL,
        LawyerProfileId INT                NOT NULL,
        Amount          DECIMAL(10,2)     NOT NULL DEFAULT (0),
        IsPaid          BIT               NOT NULL DEFAULT (0),
        RequestedAt     DATETIME2         NOT NULL DEFAULT (SYSUTCDATETIME()),
        PaidAt          DATETIME2         NULL,
        CONSTRAINT PK_PAYOUT_REQUEST PRIMARY KEY (PayoutRequestId),
        CONSTRAINT FK_PAYOUT_REQUEST_LawyerProfile FOREIGN KEY (LawyerProfileId)
            REFERENCES [dbo].[LAWYER_PROFILE] (LawyerProfileId)
    );
END
GO

/* ---------- CASE extension columns (additive, idempotent) ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CASE]') AND name = N'NotificationEmail')
    ALTER TABLE [dbo].[CASE] ADD NotificationEmail NVARCHAR(256) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CASE]') AND name = N'HasUnreadActivity')
    ALTER TABLE [dbo].[CASE] ADD HasUnreadActivity BIT NOT NULL DEFAULT (0);
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[CASE]') AND name = N'HonorariumPaid')
    ALTER TABLE [dbo].[CASE] ADD HonorariumPaid BIT NOT NULL DEFAULT (0);
GO

/* ---------- GENERATED_DOCUMENT extension columns ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]') AND name = N'VersionNo')
    ALTER TABLE [dbo].[GENERATED_DOCUMENT] ADD VersionNo INT NOT NULL DEFAULT (1);
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]') AND name = N'ClaimedAt')
    ALTER TABLE [dbo].[GENERATED_DOCUMENT] ADD ClaimedAt DATETIME2 NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[GENERATED_DOCUMENT]') AND name = N'CitizenEdited')
    ALTER TABLE [dbo].[GENERATED_DOCUMENT] ADD CitizenEdited BIT NOT NULL DEFAULT (0);
GO
