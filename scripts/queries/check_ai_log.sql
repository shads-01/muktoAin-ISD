-- MuktoAin — Dev check: AI_LOG
-- Quick sanity queries for inspecting AI request/response logs in SSMS.
-- Run with database context = MuktoAin.

USE MuktoAin;
GO

-- Row count
SELECT COUNT(*) AS LogCount FROM [dbo].[AI_LOG];
GO

-- Most recent AI calls
SELECT TOP 50
    LogId,
    CaseId,
    RequestType,
    ModelUsed,
    TokensUsed,
    LatencyMs,
    CreatedAt
FROM [dbo].[AI_LOG]
ORDER BY CreatedAt DESC;
GO

-- Usage & latency by model
SELECT
    ModelUsed,
    COUNT(*)           AS CallCount,
    SUM(TokensUsed)     AS TotalTokens,
    AVG(LatencyMs * 1.0) AS AvgLatencyMs,
    MAX(LatencyMs)      AS MaxLatencyMs
FROM [dbo].[AI_LOG]
GROUP BY ModelUsed
ORDER BY CallCount DESC;
GO

-- Usage by request type
SELECT RequestType, COUNT(*) AS CallCount, SUM(TokensUsed) AS TotalTokens
FROM [dbo].[AI_LOG]
GROUP BY RequestType
ORDER BY CallCount DESC;
GO