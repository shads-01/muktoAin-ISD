-- MuktoAin — Dev check: ACT / ACT_SECTION / ACT_SECTION_CHUNK / ACT_FOOTNOTE / SCENARIO_MAPPING
-- Quick sanity queries for inspecting the imported legal corpus in SSMS.
-- Run with database context = MuktoAin.

USE MuktoAin;
GO

-- Row counts across the whole act pipeline
SELECT 'ACT' AS TableName, COUNT(*) AS [RowCount] FROM [dbo].[ACT]
UNION ALL
SELECT 'ACT_SECTION', COUNT(*) FROM [dbo].[ACT_SECTION]
UNION ALL
SELECT 'ACT_SECTION_CHUNK', COUNT(*) FROM [dbo].[ACT_SECTION_CHUNK]
UNION ALL
SELECT 'ACT_FOOTNOTE', COUNT(*) FROM [dbo].[ACT_FOOTNOTE]
UNION ALL
SELECT 'SCENARIO_MAPPING', COUNT(*) FROM [dbo].[SCENARIO_MAPPING];
GO

-- Most recently imported acts
SELECT TOP 50
    ActId, Title, ActNumber, Year, Language, IsRepealed, TokenCount, ImportedAt
FROM [dbo].[ACT]
ORDER BY ImportedAt DESC;
GO

-- Sections for a given act (swap in a real ActId)
-- SELECT SectionId, OrdinalPosition, SectionNumber, SectionTitle
-- FROM [dbo].[ACT_SECTION]
-- WHERE ActId = 1
-- ORDER BY OrdinalPosition;

-- Chunks that still need embedding (VectorId IS NULL)
SELECT COUNT(*) AS UnembeddedChunkCount
FROM [dbo].[ACT_SECTION_CHUNK]
WHERE VectorId IS NULL;
GO

SELECT TOP 50
    asc_.ChunkId,
    asc_.SectionId,
    s.SectionNumber,
    a.Title AS ActTitle,
    asc_.ChunkOrder,
    asc_.TokenCount,
    asc_.LastEmbeddedAt
FROM [dbo].[ACT_SECTION_CHUNK] asc_
JOIN [dbo].[ACT_SECTION] s ON s.SectionId = asc_.SectionId
JOIN [dbo].[ACT] a ON a.ActId = s.ActId
WHERE asc_.VectorId IS NULL
ORDER BY asc_.ChunkId;
GO

-- Scenario keyword mappings
SELECT TOP 50
    sm.MappingId,
    sm.ScenarioKeyword,
    s.SectionNumber,
    a.Title AS ActTitle,
    sm.Notes
FROM [dbo].[SCENARIO_MAPPING] sm
JOIN [dbo].[ACT_SECTION] s ON s.SectionId = sm.SectionId
JOIN [dbo].[ACT] a ON a.ActId = s.ActId
ORDER BY sm.MappingId DESC;
GO
