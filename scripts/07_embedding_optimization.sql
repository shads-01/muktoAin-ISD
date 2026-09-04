-- 07_embedding_optimization.sql — one-time maintenance for the embedding
-- ingestion pipeline (run AFTER deploying the optimized EmbeddingBatchJob).
--
-- What this does:
--   1. Deletes junk chunk rows (empty / whitespace / <20 chars) that carry no
--      legal meaning yet would still burn Gemini free-tier quota. 41 of the
--      53 junk rows are still unembedded; the 12 already-embedded ones are
--      deleted too because their vectors are noise in the collection.
--      NOTE: their stale Qdrant points (if any) are harmless — retrieval
--      re-hydrates sections from SQL and a deleted ChunkId just fails the
--      section re-hydration lookup and is skipped (SimilaritySearchService
--      guards missing sections already).
--   2. Reports (does NOT touch) duplicate-text rows: the reworked batch job
--      now deduplicates identical ChunkText in-run and stamps them all with
--      the same VectorId, so no data change is needed for duplicates.
--   3. Reseed IDENTITY so ChunkId stays contiguous for observability.
--
-- Idempotent: re-running deletes 0 rows and prints the same report.

USE MuktoAin;
GO

SET NOCOUNT ON;
-- The filtered index IX_ACT_SECTION_CHUNK_VectorId_Null requires these SET
-- options for any DML against the table.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

DECLARE @JunkDeleted INT;

BEGIN TRANSACTION;

DELETE FROM dbo.ACT_SECTION_CHUNK
WHERE  LTRIM(RTRIM(ChunkText)) = N''
   OR  LEN(ChunkText) < 20;

SET @JunkDeleted = @@ROWCOUNT;

COMMIT;

PRINT CONCAT('Junk chunk rows deleted: ', @JunkDeleted);

-- Reclaim the IDs so the batch job log/ETA arithmetic stays readable
-- (cosmetic; safe to skip on a shared server).
DBCC CHECKIDENT ('dbo.ACT_SECTION_CHUNK', RESEED);
GO

-- Post-cleanup state report
SELECT
    COUNT(*)                                            AS TotalChunks,
    SUM(CASE WHEN VectorId IS NULL THEN 1 ELSE 0 END)   AS Unembedded,
    SUM(CASE WHEN VectorId IS NOT NULL THEN 1 ELSE 0 END) AS Embedded,
    SUM(CASE WHEN LEN(ChunkText) < 20 THEN 1 ELSE 0 END)  AS JunkRemaining
FROM dbo.ACT_SECTION_CHUNK;

-- Duplicate-text census among unembedded rows (the batch job collapses these
-- in-run now; this is informational only).
SELECT
    COUNT(*) AS DuplicateTextGroups,
    SUM(Cnt) AS DuplicateTextRows
FROM (
    SELECT CHECKSUM(ChunkText) AS Cs, COUNT(*) AS Cnt
    FROM dbo.ACT_SECTION_CHUNK
    WHERE VectorId IS NULL
    GROUP BY CHECKSUM(ChunkText)
    HAVING COUNT(*) > 1
) t;
GO
