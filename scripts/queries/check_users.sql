-- MuktoAin — Dev check: USER / LAWYER_PROFILE
-- Quick sanity queries for inspecting user accounts in SSMS.
-- Run with database context = MuktoAin.

USE MuktoAin;
GO

-- Row count
SELECT COUNT(*) AS UserCount FROM [dbo].[USER];
GO

-- Most recent users first
SELECT TOP 50
    UserId,
    FullName,
    Email,
    Role,
    AccountStatus,
    PreferredLanguage,
    PhoneNumber,
    EmailConfirmed,
    CreatedByAdminId,
    CreatedAt
FROM [dbo].[USER]
ORDER BY CreatedAt DESC;
GO

-- Users grouped by role / status
SELECT Role, AccountStatus, COUNT(*) AS Total
FROM [dbo].[USER]
GROUP BY Role, AccountStatus
ORDER BY Role, AccountStatus;
GO

-- Lawyer profiles joined to their user record
SELECT
    lp.LawyerProfileId,
    u.UserId,
    u.FullName,
    u.Email,
    lp.BarRegistrationNumber,
    lp.VerificationStatus,
    lp.Specialization,
    lp.VerifiedByAdminId,
    lp.VerifiedAt
FROM [dbo].[LAWYER_PROFILE] lp
JOIN [dbo].[USER] u ON u.UserId = lp.UserId
ORDER BY lp.LawyerProfileId DESC;
GO