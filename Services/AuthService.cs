using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using sudokuvip.Database;
using sudokuvip.Models;

namespace sudokuvip.Services
{
    public static class AuthService
    {
        public static UserAccount? CurrentUser { get; private set; }
        public static event Action? CurrentUserChanged;

        // Lưu trữ lịch sử tạm thời cho khách trong phiên chơi hiện tại
        public static List<GameHistoryRecord> GuestHistory { get; } = new List<GameHistoryRecord>();

        public static string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            var builder = new StringBuilder();
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        public static bool CheckUsernameExists(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                conn.Open();
                string sql = "SELECT COUNT(1) FROM Users WHERE LOWER(Username) = LOWER(@Username)";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Username", username.Trim());
                return (int)cmd.ExecuteScalar()! > 0;
            }
            catch
            {
                return false;
            }
        }

        public static (bool Success, string Message, UserAccount? User) Register(
            string username, 
            string password, 
            string displayName, 
            string avatar = "👤",
            bool migrateGuestData = false)
        {
            username = username.Trim();
            displayName = displayName.Trim();
            if (string.IsNullOrWhiteSpace(avatar)) avatar = "👤";

            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
                return (false, "Tên đăng nhập phải có ít nhất 3 ký tự.", null);

            if (username.Any(ch => !char.IsLetterOrDigit(ch) && ch != '_'))
                return (false, "Tên đăng nhập chỉ gồm chữ cái, số và dấu gạch dưới (_).", null);

            if (string.IsNullOrWhiteSpace(password) || password.Length < 4)
                return (false, "Mật khẩu phải có ít nhất 4 ký tự.", null);

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = username;

            try
            {
                using var conn = DatabaseHelper.GetConnection();
                conn.Open();

                // Kiểm tra username trùng lặp
                string checkSql = "SELECT COUNT(1) FROM Users WHERE LOWER(Username) = LOWER(@Username)";
                using (var checkCmd = new SqlCommand(checkSql, conn))
                {
                    checkCmd.Parameters.AddWithValue("@Username", username);
                    int count = (int)checkCmd.ExecuteScalar()!;
                    if (count > 0)
                    {
                        return (false, "Tên tài khoản này đã được sử dụng. Vui lòng chọn tên khác.", null);
                    }
                }

                // Tính toán số liệu khởi tạo (nếu chuyển đổi từ khách)
                int initialHighScore = 0;
                int initialTotalScore = 0;
                int initialTotalGames = 0;
                int initialTotalWins = 0;

                if (migrateGuestData && CurrentUser != null && CurrentUser.IsGuest)
                {
                    initialHighScore = CurrentUser.HighScore;
                    initialTotalScore = CurrentUser.TotalScore;
                    initialTotalGames = CurrentUser.TotalGames;
                    initialTotalWins = CurrentUser.TotalWins;
                }

                string hash = HashPassword(password);
                string insertSql = @"
                    INSERT INTO Users (Username, PasswordHash, DisplayName, Avatar, CreatedAt, HighScore, TotalScore, TotalGames, TotalWins)
                    OUTPUT INSERTED.UserId, INSERTED.CreatedAt
                    VALUES (@Username, @PasswordHash, @DisplayName, @Avatar, GETDATE(), @HighScore, @TotalScore, @TotalGames, @TotalWins);";

                using var trans = conn.BeginTransaction();

                int newUserId;
                DateTime createdAt;

                using (var insertCmd = new SqlCommand(insertSql, conn, trans))
                {
                    insertCmd.Parameters.AddWithValue("@Username", username);
                    insertCmd.Parameters.AddWithValue("@PasswordHash", hash);
                    insertCmd.Parameters.AddWithValue("@DisplayName", displayName);
                    insertCmd.Parameters.AddWithValue("@Avatar", avatar);
                    insertCmd.Parameters.AddWithValue("@HighScore", initialHighScore);
                    insertCmd.Parameters.AddWithValue("@TotalScore", initialTotalScore);
                    insertCmd.Parameters.AddWithValue("@TotalGames", initialTotalGames);
                    insertCmd.Parameters.AddWithValue("@TotalWins", initialTotalWins);

                    using var reader = insertCmd.ExecuteReader();
                    if (!reader.Read())
                    {
                        trans.Rollback();
                        return (false, "Không thể tạo tài khoản, vui lòng thử lại.", null);
                    }

                    newUserId = reader.GetInt32(0);
                    createdAt = reader.GetDateTime(1);
                }

                // Nếu có chuyển đổi dữ liệu từ khách, di chuyển lịch sử GuestHistory vào GameHistory trong CSDL
                if (migrateGuestData && GuestHistory.Count > 0)
                {
                    foreach (var h in GuestHistory)
                    {
                        string insertHistorySql = @"
                            INSERT INTO GameHistory (UserId, Difficulty, Score, DurationSeconds, Mistakes, IsWin, PlayedAt)
                            VALUES (@UserId, @Difficulty, @Score, @DurationSeconds, @Mistakes, @IsWin, @PlayedAt);";

                        using var histCmd = new SqlCommand(insertHistorySql, conn, trans);
                        histCmd.Parameters.AddWithValue("@UserId", newUserId);
                        histCmd.Parameters.AddWithValue("@Difficulty", h.Difficulty);
                        histCmd.Parameters.AddWithValue("@Score", h.Score);
                        histCmd.Parameters.AddWithValue("@DurationSeconds", h.DurationSeconds);
                        histCmd.Parameters.AddWithValue("@Mistakes", h.Mistakes);
                        histCmd.Parameters.AddWithValue("@IsWin", h.IsWin);
                        histCmd.Parameters.AddWithValue("@PlayedAt", h.PlayedAt);
                        histCmd.ExecuteNonQuery();
                    }
                    GuestHistory.Clear();
                }

                trans.Commit();

                var newUser = new UserAccount
                {
                    UserId = newUserId,
                    Username = username,
                    DisplayName = displayName,
                    Avatar = avatar,
                    CreatedAt = createdAt,
                    HighScore = initialHighScore,
                    TotalScore = initialTotalScore,
                    TotalGames = initialTotalGames,
                    TotalWins = initialTotalWins,
                    IsGuest = false
                };

                CurrentUser = newUser;
                CurrentUserChanged?.Invoke();
                return (true, "Tạo tài khoản mới thành công!", newUser);
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi cơ sở dữ liệu: {ex.Message}", null);
            }
        }

        public static (bool Success, string Message, UserAccount? User) Login(string username, string password)
        {
            username = username.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return (false, "Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.", null);

            try
            {
                using var conn = DatabaseHelper.GetConnection();
                conn.Open();

                string hash = HashPassword(password);
                string sql = @"
                    SELECT UserId, Username, DisplayName, 
                           ISNULL(Avatar, N'👤') AS Avatar, 
                           CreatedAt, HighScore, TotalScore, TotalGames, TotalWins
                    FROM Users 
                    WHERE LOWER(Username) = LOWER(@Username) AND PasswordHash = @PasswordHash";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Username", username);
                cmd.Parameters.AddWithValue("@PasswordHash", hash);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    var user = new UserAccount
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        DisplayName = reader.GetString(2),
                        Avatar = reader.GetString(3),
                        CreatedAt = reader.GetDateTime(4),
                        HighScore = reader.GetInt32(5),
                        TotalScore = reader.GetInt32(6),
                        TotalGames = reader.GetInt32(7),
                        TotalWins = reader.GetInt32(8),
                        IsGuest = false
                    };

                    CurrentUser = user;
                    CurrentUserChanged?.Invoke();
                    return (true, "Đăng nhập thành công!", user);
                }
                else
                {
                    return (false, "Sai tên đăng nhập hoặc mật khẩu!", null);
                }
            }
            catch (Exception ex)
            {
                return (false, $"Lỗi kết nối cơ sở dữ liệu: {ex.Message}", null);
            }
        }

        public static void LoginAsGuest(string? customNickname = null, string? customAvatar = null)
        {
            string name = string.IsNullOrWhiteSpace(customNickname) ? GenerateGuestName() : customNickname.Trim();
            string avatar = string.IsNullOrWhiteSpace(customAvatar) ? "👤" : customAvatar;

            // Nếu khách cũ tiếp tục chơi, giữ lại thành tích tạm
            if (CurrentUser != null && CurrentUser.IsGuest)
            {
                CurrentUser.DisplayName = name;
                CurrentUser.Avatar = avatar;
            }
            else
            {
                CurrentUser = new UserAccount
                {
                    UserId = 0,
                    Username = "guest",
                    DisplayName = name,
                    Avatar = avatar,
                    CreatedAt = DateTime.Now,
                    HighScore = 0,
                    TotalScore = 0,
                    TotalGames = 0,
                    TotalWins = 0,
                    IsGuest = true
                };
            }

            CurrentUserChanged?.Invoke();
        }

        public static string GenerateGuestName()
        {
            var adjectives = new[] { "Khách", "Tân thủ", "Cao thủ", "Người chơi", "Ninja", "Chiến binh" };
            var rnd = new Random();
            string adj = adjectives[rnd.Next(adjectives.Length)];
            int number = rnd.Next(100, 999);
            return $"{adj} #{number}";
        }

        public static void Logout()
        {
            CurrentUser = null;
            CurrentUserChanged?.Invoke();
        }

        public static void RefreshCurrentUser()
        {
            if (CurrentUser == null || CurrentUser.IsGuest) return;

            try
            {
                using var conn = DatabaseHelper.GetConnection();
                conn.Open();

                string sql = "SELECT DisplayName, ISNULL(Avatar, N'👤'), HighScore, TotalScore, TotalGames, TotalWins FROM Users WHERE UserId = @UserId";
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@UserId", CurrentUser.UserId);

                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    CurrentUser.DisplayName = reader.GetString(0);
                    CurrentUser.Avatar = reader.GetString(1);
                    CurrentUser.HighScore = reader.GetInt32(2);
                    CurrentUser.TotalScore = reader.GetInt32(3);
                    CurrentUser.TotalGames = reader.GetInt32(4);
                    CurrentUser.TotalWins = reader.GetInt32(5);
                    CurrentUserChanged?.Invoke();
                }
            }
            catch
            {
                // Bỏ qua lỗi tạm thời
            }
        }

        public static bool RecordGameResult(string difficulty, int score, int durationSeconds, int mistakes, bool isWin)
        {
            if (CurrentUser == null) return false;

            // Nếu là khách: ghi vào GuestHistory tạm thời
            if (CurrentUser.IsGuest)
            {
                CurrentUser.TotalGames++;
                if (isWin) CurrentUser.TotalWins++;
                CurrentUser.TotalScore += score;
                if (score > CurrentUser.HighScore) CurrentUser.HighScore = score;

                GuestHistory.Add(new GameHistoryRecord
                {
                    HistoryId = GuestHistory.Count + 1,
                    UserId = 0,
                    Difficulty = difficulty,
                    Score = score,
                    DurationSeconds = durationSeconds,
                    Mistakes = mistakes,
                    IsWin = isWin,
                    PlayedAt = DateTime.Now
                });

                CurrentUserChanged?.Invoke();
                return true;
            }

            try
            {
                using var conn = DatabaseHelper.GetConnection();
                conn.Open();

                using var trans = conn.BeginTransaction();

                // 1. Thêm vào bảng GameHistory
                string insertHistorySql = @"
                    INSERT INTO GameHistory (UserId, Difficulty, Score, DurationSeconds, Mistakes, IsWin, PlayedAt)
                    VALUES (@UserId, @Difficulty, @Score, @DurationSeconds, @Mistakes, @IsWin, GETDATE());";

                using (var cmd = new SqlCommand(insertHistorySql, conn, trans))
                {
                    cmd.Parameters.AddWithValue("@UserId", CurrentUser.UserId);
                    cmd.Parameters.AddWithValue("@Difficulty", difficulty);
                    cmd.Parameters.AddWithValue("@Score", score);
                    cmd.Parameters.AddWithValue("@DurationSeconds", durationSeconds);
                    cmd.Parameters.AddWithValue("@Mistakes", mistakes);
                    cmd.Parameters.AddWithValue("@IsWin", isWin);
                    cmd.ExecuteNonQuery();
                }

                // 2. Cập nhật bảng Users
                string updateUserSql = @"
                    UPDATE Users
                    SET TotalGames = TotalGames + 1,
                        TotalWins = TotalWins + CASE WHEN @IsWin = 1 THEN 1 ELSE 0 END,
                        TotalScore = TotalScore + @Score,
                        HighScore = CASE WHEN @Score > HighScore THEN @Score ELSE HighScore END
                    WHERE UserId = @UserId;";

                using (var cmd = new SqlCommand(updateUserSql, conn, trans))
                {
                    cmd.Parameters.AddWithValue("@UserId", CurrentUser.UserId);
                    cmd.Parameters.AddWithValue("@Score", score);
                    cmd.Parameters.AddWithValue("@IsWin", isWin ? 1 : 0);
                    cmd.ExecuteNonQuery();
                }

                trans.Commit();

                // Cập nhật model in-memory
                CurrentUser.TotalGames++;
                if (isWin) CurrentUser.TotalWins++;
                CurrentUser.TotalScore += score;
                if (score > CurrentUser.HighScore) CurrentUser.HighScore = score;

                CurrentUserChanged?.Invoke();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static List<GameHistoryRecord> GetUserHistory(int userId)
        {
            // Nếu là khách: trả về danh sách lịch sử trong phiên
            if (userId == 0 || (CurrentUser != null && CurrentUser.IsGuest))
            {
                return GuestHistory.OrderByDescending(x => x.PlayedAt).ToList();
            }

            var list = new List<GameHistoryRecord>();
            try
            {
                using var conn = DatabaseHelper.GetConnection();
                conn.Open();

                string sql = @"
                    SELECT HistoryId, UserId, Difficulty, Score, DurationSeconds, Mistakes, IsWin, PlayedAt
                    FROM GameHistory
                    WHERE UserId = @UserId
                    ORDER BY PlayedAt DESC";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@UserId", userId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new GameHistoryRecord
                    {
                        HistoryId = reader.GetInt32(0),
                        UserId = reader.GetInt32(1),
                        Difficulty = reader.GetString(2),
                        Score = reader.GetInt32(3),
                        DurationSeconds = reader.GetInt32(4),
                        Mistakes = reader.GetInt32(5),
                        IsWin = reader.GetBoolean(6),
                        PlayedAt = reader.GetDateTime(7)
                    });
                }
            }
            catch
            {
                // Bỏ qua lỗi
            }

            return list;
        }
    }
}
