-- scripts/20_ai_chat_credits.sql
-- Per-user chat quota + paid chat credits:
--   * CHAT_TURN: one row per metered chat turn. UserId NULL = guest (one
--     shared guest pool). PaidWithCredit = 1 when the turn was paid with a
--     chat credit because the free daily turns had run out. Written and
--     counted by AiTurnReservationStore in single atomic statements; the row
--     is deleted again when the turn turns out to be free (cache hit,
--     retrieval-only, blocked).
--   * PAYMENT_ORDER.ChatCredits: credits a TopUp order is worth
--     (Amount / PaymentService.ChatCreditPrice). Counted into the balance
--     while the order is Paid or Refunded; a refund lowers it by the credits
--     still unused, so credits already spent stay spent.
-- Credit balance = SUM(ChatCredits of Paid/Refunded TopUp orders)
--                - COUNT(CHAT_TURN rows with PaidWithCredit = 1).
-- Idempotent: safe to re-run.
USE [MuktoAin];
GO
SET NOCOUNT ON;
GO
-- PAYMENT_ORDER has a filtered index (script 18); ALTER TABLE needs this.
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[dbo].[CHAT_TURN]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[CHAT_TURN]
    (
        ChatTurnId     BIGINT IDENTITY(1,1) NOT NULL,
        UserId         INT                  NULL,
        PaidWithCredit BIT                  NOT NULL CONSTRAINT DF_CHAT_TURN_PaidWithCredit DEFAULT (0),
        CreatedAt      DATETIME2            NOT NULL CONSTRAINT DF_CHAT_TURN_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT PK_CHAT_TURN PRIMARY KEY CLUSTERED (ChatTurnId),
        CONSTRAINT FK_CHAT_TURN_USER FOREIGN KEY (UserId) REFERENCES [dbo].[USER] (UserId)
    );
    -- Serves both the daily free count and the all-time credit count; the
    -- reservation's UPDLOCK/HOLDLOCK range lock sits on this index.
    CREATE INDEX IX_CHAT_TURN_User_Credit_Created
        ON [dbo].[CHAT_TURN] (UserId, PaidWithCredit, CreatedAt);
    PRINT 'CHAT_TURN created.';
END
ELSE
    PRINT 'CHAT_TURN already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]')
                 AND name = N'ChatCredits')
BEGIN
    ALTER TABLE [dbo].[PAYMENT_ORDER] ADD [ChatCredits] INT NOT NULL
        CONSTRAINT DF_PAYMENT_ORDER_ChatCredits DEFAULT (0);
    PRINT 'PAYMENT_ORDER.ChatCredits added.';
END
ELSE
    PRINT 'PAYMENT_ORDER.ChatCredits already exists.';
GO
