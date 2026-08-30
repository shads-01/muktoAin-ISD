-- MuktoAin — Dev check: GENERATED_DOCUMENT / LAWYER_REVIEW
-- Quick sanity queries for inspecting generated documents in SSMS.
-- Run with database context = MuktoAin.

USE MuktoAin;
GO

-- Row count
SELECT COUNT(*) AS DocumentCount FROM [dbo].[GENERATED_DOCUMENT];
GO

-- Most recent documents, with case + assigned lawyer resolved
SELECT TOP 50
    gd.DocumentId,
    gd.CaseId,
    c.Title           AS CaseTitle,
    gd.DocumentType,
    gd.Status,
    gd.AssignedLawyerProfileId,
    u.FullName         AS AssignedLawyerName,
    gd.PdfPath,
    gd.CreatedAt
FROM [dbo].[GENERATED_DOCUMENT] gd
JOIN [dbo].[CASE] c ON c.CaseId = gd.CaseId
LEFT JOIN [dbo].[LAWYER_PROFILE] lp ON lp.LawyerProfileId = gd.AssignedLawyerProfileId
LEFT JOIN [dbo].[USER] u ON u.UserId = lp.UserId
ORDER BY gd.CreatedAt DESC;
GO

-- Documents grouped by status
SELECT Status, COUNT(*) AS Total
FROM [dbo].[GENERATED_DOCUMENT]
GROUP BY Status
ORDER BY Status;
GO

-- Documents awaiting review (no lawyer assigned yet)
SELECT DocumentId, CaseId, DocumentType, Status, CreatedAt
FROM [dbo].[GENERATED_DOCUMENT]
WHERE AssignedLawyerProfileId IS NULL
ORDER BY CreatedAt ASC;
GO

-- Lawyer review history
SELECT
    lr.ReviewId,
    lr.DocumentId,
    u.FullName AS LawyerName,
    lr.Decision,
    lr.Comments,
    lr.ReviewedAt
FROM [dbo].[LAWYER_REVIEW] lr
JOIN [dbo].[LAWYER_PROFILE] lp ON lp.LawyerProfileId = lr.LawyerProfileId
JOIN [dbo].[USER] u ON u.UserId = lp.UserId
ORDER BY lr.ReviewedAt DESC;
GO
