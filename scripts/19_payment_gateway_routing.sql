-- scripts/19_payment_gateway_routing.sql
-- Per-order payment gateway (bKash sandbox + SSLCommerz sandbox + simulator):
--   * Gateway: which gateway the order was sent to (PaymentGateway enum:
--     0 Simulator, 1 SslCommerz, 2 Bkash). The payment is validated by the
--     same gateway even if Payments:Mode changes meanwhile.
--   * GatewaySessionId: the gateway's own checkout id (bKash paymentID). The
--     bKash callback identifies the payment by it, not by our tran_id.
-- Idempotent: safe to re-run.
USE [MuktoAin];
GO
SET NOCOUNT ON;
GO
-- PAYMENT_ORDER has a filtered index (script 18); ALTER TABLE needs this.
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]')
                 AND name = N'Gateway')
BEGIN
    ALTER TABLE [dbo].[PAYMENT_ORDER] ADD [Gateway] INT NOT NULL
        CONSTRAINT DF_PAYMENT_ORDER_Gateway DEFAULT (0);
    PRINT 'PAYMENT_ORDER.Gateway added.';
END
ELSE
    PRINT 'PAYMENT_ORDER.Gateway already exists.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[PAYMENT_ORDER]')
                 AND name = N'GatewaySessionId')
BEGIN
    ALTER TABLE [dbo].[PAYMENT_ORDER] ADD [GatewaySessionId] NVARCHAR(100) NULL;
    PRINT 'PAYMENT_ORDER.GatewaySessionId added.';
END
ELSE
    PRINT 'PAYMENT_ORDER.GatewaySessionId already exists.';
GO
