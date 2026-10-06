-- Manual bootstrap only. Select an explicitly created empty database before running.
-- Do not execute this automatically on application startup.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.Users',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Users (
        UserId INT IDENTITY(1,1) PRIMARY KEY,
        Username NVARCHAR(50) NOT NULL UNIQUE,
        PasswordHash NVARCHAR(256) NOT NULL,
        DisplayName NVARCHAR(100) NOT NULL,
        Avatar NVARCHAR(20) NOT NULL DEFAULT N'👤',
        CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        HighScore INT NOT NULL DEFAULT 0,
        TotalScore INT NOT NULL DEFAULT 0,
        TotalGames INT NOT NULL DEFAULT 0,
        TotalWins INT NOT NULL DEFAULT 0
    );
END;
IF OBJECT_ID(N'dbo.GameHistory',N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GameHistory (
        HistoryId INT IDENTITY(1,1) PRIMARY KEY,
        UserId INT NOT NULL,
        Difficulty NVARCHAR(30) NOT NULL,
        Score INT NOT NULL,
        DurationSeconds INT NOT NULL,
        Mistakes INT NOT NULL,
        IsWin BIT NOT NULL,
        PlayedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_GameHistory_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(UserId) ON DELETE CASCADE
    );
END;
COMMIT;
-- Next, run migrations/001_game_identity.sql in this same selected database.

-- Then run migrations/002_pvp_history.sql.
