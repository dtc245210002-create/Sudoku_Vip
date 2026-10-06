-- Manual migration after 001_game_identity.sql, in the explicitly selected database.
-- Existing RAM-only PvP results cannot be reconstructed.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF COL_LENGTH('dbo.Users','EloRating') IS NULL
    ALTER TABLE dbo.Users ADD EloRating INT NOT NULL CONSTRAINT DF_Users_Elo DEFAULT 1200;
IF COL_LENGTH('dbo.Users','PvpGames') IS NULL
    ALTER TABLE dbo.Users ADD PvpGames INT NOT NULL CONSTRAINT DF_Users_PvpGames DEFAULT 0;
IF COL_LENGTH('dbo.Users','PvpWins') IS NULL
    ALTER TABLE dbo.Users ADD PvpWins INT NOT NULL CONSTRAINT DF_Users_PvpWins DEFAULT 0;
IF COL_LENGTH('dbo.GameHistory','IsPvp') IS NULL
    ALTER TABLE dbo.GameHistory ADD IsPvp BIT NOT NULL CONSTRAINT DF_History_Pvp DEFAULT 0;
IF COL_LENGTH('dbo.GameHistory','MatchId') IS NULL
    ALTER TABLE dbo.GameHistory ADD MatchId NVARCHAR(64) NOT NULL CONSTRAINT DF_History_Match DEFAULT N'';
IF COL_LENGTH('dbo.GameHistory','Opponent') IS NULL
    ALTER TABLE dbo.GameHistory ADD Opponent NVARCHAR(100) NOT NULL CONSTRAINT DF_History_Opponent DEFAULT N'';
IF COL_LENGTH('dbo.GameHistory','EloAfter') IS NULL
    ALTER TABLE dbo.GameHistory ADD EloAfter INT NOT NULL CONSTRAINT DF_History_Elo DEFAULT 1200;
COMMIT;
