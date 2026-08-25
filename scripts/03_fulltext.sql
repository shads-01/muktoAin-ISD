-- MuktoAin — SQL Server Full-Text Search
-- Run after 02_schema.sql, in SSMS, with database context = MuktoAin.
-- Requires the "Full-Text and Semantic Extractions for Search" feature to be
-- installed on the SQL Server instance (see _Initial_setup_plan.md §1.2).

USE MuktoAin;
GO

-- 1. Create Full-Text Catalog if not exists
IF NOT EXISTS (SELECT * FROM sys.fulltext_catalogs WHERE name = 'MuktoAinCatalog')
BEGIN
    CREATE FULLTEXT CATALOG MuktoAinCatalog AS DEFAULT;
END
GO

-- 2. Create Full-Text Index on ACT_SECTION
-- NOTE: ACT_SECTION has NO ActTitle column (title lives on [dbo].[ACT]).
-- Index SectionText only; filter/join by Act title at query time via
-- IActRepository.GetWithSectionsAsync or a JOIN in the FTS query.
IF NOT EXISTS (
    SELECT * FROM sys.fulltext_indexes
    WHERE object_id = OBJECT_ID('[dbo].[ACT_SECTION]')
)
BEGIN
    CREATE FULLTEXT INDEX ON [dbo].[ACT_SECTION](SectionText)
        KEY INDEX PK_ACT_SECTION
        ON MuktoAinCatalog
        WITH STOPLIST = OFF;
END
GO

-- Verify: SELECT FULLTEXTCATALOGPROPERTY('MuktoAinCatalog', 'ItemCount') AS IndexedItemCount;
