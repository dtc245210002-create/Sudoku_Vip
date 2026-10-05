using System;
using Microsoft.Data.SqlClient;

namespace sudokuvip.Database
{
    public static class DatabaseHelper
    {
        public const string ServerName = "LAPTOP-FLO3DB3M";
        public const string DatabaseName = "SudokuVIPDB";

        private static string MasterConnectionString =>
            $"Server={ServerName};Database=master;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10;";

        public static string AppConnectionString =>
            $"Server={ServerName};Database={DatabaseName};Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10;";

        public static SqlConnection GetConnection()
        {
            return new SqlConnection(AppConnectionString);
        }

        public static (bool Success, string Message) EnsureDatabaseAndTablesExist()
        {
            try
            {
                // 1. Kết nối master để kiểm tra và tạo database SudokuVIPDB nếu chưa có
                using (var masterConn = new SqlConnection(MasterConnectionString))
                {
                    masterConn.Open();
                    string createDbSql = $@"
                        IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'{DatabaseName}')
                        BEGIN
                            CREATE DATABASE [{DatabaseName}];
                        END";
                    using var cmd = new SqlCommand(createDbSql, masterConn);
                    cmd.ExecuteNonQuery();
                }

                // 2. Kết nối tới SudokuVIPDB để tạo các bảng
                using (var appConn = new SqlConnection(AppConnectionString))
                {
                    appConn.Open();

                    string createTablesSql = @"
                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users')
                        BEGIN
                            CREATE TABLE Users (
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
                        END
                        ELSE
                        BEGIN
                            IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Users') AND name = 'Avatar')
                            BEGIN
                                ALTER TABLE Users ADD Avatar NVARCHAR(20) NOT NULL DEFAULT N'👤';
                            END
                        END;

                        IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'GameHistory')
                        BEGIN
                            CREATE TABLE GameHistory (
                                HistoryId INT IDENTITY(1,1) PRIMARY KEY,
                                UserId INT NOT NULL,
                                Difficulty NVARCHAR(30) NOT NULL,
                                Score INT NOT NULL,
                                DurationSeconds INT NOT NULL,
                                Mistakes INT NOT NULL,
                                IsWin BIT NOT NULL,
                                PlayedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                                CONSTRAINT FK_GameHistory_Users FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE
                            );
                        END;";

                    using var cmd = new SqlCommand(createTablesSql, appConn);
                    cmd.ExecuteNonQuery();
                }

                return (true, "Kết nối và khởi tạo cơ sở dữ liệu SQL Server thành công.");
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi kết nối SQL Server ({ServerName}): {ex.Message}");
            }
        }
    }
}
