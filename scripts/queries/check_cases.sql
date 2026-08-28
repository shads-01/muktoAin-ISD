-- MuktoAin — Dev check: CASE / CASE_CATEGORY / DISTRICT / CASE_ACT_REFERENCE
-- Quick sanity queries for inspecting cases in SSMS.
-- Run with database context = MuktoAin.

USE MuktoAin;
GO

-- Most recent cases, with category/district names resolved
SELECT TOP 50
    c.CaseId,
    c.Title,
    c.Status,
    c.Language,
    c.IsAnonymous,
    c.AnonymousTrackingCode,
    cc.Name  AS CategoryName,
    d.Name   AS DistrictName,
    c.UserId,
    u.Email  AS UserEmail,
    c.CreatedAt,
    c.UpdatedAt
FROM [dbo].[CASE] c
JOIN [dbo].[CASE_CATEGORY] cc ON cc.CategoryId = c.CategoryId
JOIN [dbo].[DISTRICT] d       ON d.DistrictId = c.DistrictId
LEFT JOIN [dbo].[USER] u      ON u.UserId = c.UserId
ORDER BY c.CreatedAt DESC;
GO

-- Cases grouped by status
SELECT Status, COUNT(*) AS Total
FROM [dbo].[CASE]
GROUP BY Status
ORDER BY Status;
GO

-- Cases grouped by category
SELECT cc.Name AS CategoryName, COUNT(*) AS Total
FROM [dbo].[CASE] c
JOIN [dbo].[CASE_CATEGORY] cc ON cc.CategoryId = c.CategoryId
GROUP BY cc.Name
ORDER BY Total DESC;
GO

-- Anonymous (guest) cases
SELECT CaseId, Title, AnonymousTrackingCode, CreatedAt
FROM [dbo].[CASE]
WHERE IsAnonymous = 1
ORDER BY CreatedAt DESC;
GO

-- Act sections referenced by a case (most recent case shown as example)
SELECT
    car.CaseId,
    car.SectionId,
    s.SectionNumber,
    s.SectionTitle,
    a.Title AS ActTitle,
    car.RelevanceScore,
    car.RetrievalMethod
FROM [dbo].[CASE_ACT_REFERENCE] car
JOIN [dbo].[ACT_SECTION] s ON s.SectionId = car.SectionId
JOIN [dbo].[ACT] a         ON a.ActId = s.ActId
WHERE car.CaseId = (SELECT MAX(CaseId) FROM [dbo].[CASE])
ORDER BY car.RelevanceScore DESC;
GO
