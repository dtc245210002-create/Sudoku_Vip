-- Review and run manually on a confirmed test database first. Back up before production use.
-- Never deletes accounts or history. Requires existing Users and GameHistory tables.
SET XACT_ABORT ON;
BEGIN TRY
    BEGIN TRANSACTION;
    IF OBJECT_ID(N'dbo.Users',N'U') IS NULL OR OBJECT_ID(N'dbo.GameHistory',N'U') IS NULL
        THROW 51000, 'Run setup.sql first on the selected empty database.', 1;
    IF EXISTS (SELECT LOWER(Username) FROM dbo.Users GROUP BY LOWER(Username) HAVING COUNT(*) > 1)
        THROW 51001, 'Case-insensitive username collisions require manual resolution. No changes applied.', 1;
    IF COL_LENGTH('dbo.Users','Avatar') IS NULL
        EXEC(N'ALTER TABLE dbo.Users ADD Avatar NVARCHAR(20) NOT NULL DEFAULT N''👤''');
    IF COL_LENGTH('dbo.GameHistory','GameId') IS NULL
        EXEC(N'ALTER TABLE dbo.GameHistory ADD GameId UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_GameHistory_GameId DEFAULT NEWID() WITH VALUES');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.GameHistory') AND name='UX_GameHistory_GameId')
        EXEC(N'CREATE UNIQUE INDEX UX_GameHistory_GameId ON dbo.GameHistory(GameId)');
    IF COL_LENGTH('dbo.Users','UsernameKey') IS NULL
        EXEC(N'ALTER TABLE dbo.Users ADD UsernameKey AS LOWER(Username) PERSISTED');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Users') AND name='UX_Users_UsernameKey')
        EXEC(N'CREATE UNIQUE INDEX UX_Users_UsernameKey ON dbo.Users(UsernameKey)');
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.GameHistory') AND name='IX_GameHistory_UserId')
        CREATE INDEX IX_GameHistory_UserId ON dbo.GameHistory(UserId);
    UPDATE u SET TotalGames=h.Games,TotalWins=h.Wins,TotalScore=h.Score,HighScore=h.High
    FROM dbo.Users u CROSS APPLY (
        SELECT COUNT(*) Games,COALESCE(SUM(CASE WHEN IsWin=1 THEN 1 ELSE 0 END),0) Wins,
        COALESCE(SUM(Score),0) Score,COALESCE(MAX(Score),0) High
        FROM dbo.GameHistory WHERE UserId=u.UserId
    ) h;
    COMMIT;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK;
    THROW;
END CATCH;
