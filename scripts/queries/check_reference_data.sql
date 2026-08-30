-- MuktoAin — Dev check: DISTRICT / CASE_CATEGORY (reference/lookup tables)
-- Quick sanity queries to confirm seed/lookup data loaded correctly in SSMS.
-- Run with database context = MuktoAin.

USE MuktoAin;
GO

-- Districts
SELECT COUNT(*) AS DistrictCount FROM [dbo].[DISTRICT];
GO

SELECT DistrictId, Name
FROM [dbo].[DISTRICT]
ORDER BY DistrictId;
GO

-- Case categories
SELECT COUNT(*) AS CategoryCount FROM [dbo].[CASE_CATEGORY];
GO

SELECT
    CategoryId,
    Name,
    NameBn,
    Description,
    DescriptionBn,
    CommonActions,
    CommonActionsEn
FROM [dbo].[CASE_CATEGORY]
ORDER BY CategoryId;
GO