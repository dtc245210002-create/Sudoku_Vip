using sudokuvip.Models;

namespace sudokuvip.Services;

public enum UsernameStatus { Available, Exists, Error, Invalid }

public static class AuthService
{
    public static UserAccount? CurrentUser { get; private set; }
    public static event Action? CurrentUserChanged;
    public static IAccountStore Store { get; set; } = new SqlAccountStore();
    private static readonly SemaphoreSlim Operations = new(1,1);
    private static readonly Dictionary<Guid,List<GameHistoryRecord>> GuestSessions = new();
    private static readonly HashSet<Guid> MigratedSessions = new();
    public static IReadOnlyList<GameHistoryRecord> GuestHistory => CurrentUser?.IsGuest == true
        ? HistoryFor(CurrentUser).AsReadOnly() : Array.Empty<GameHistoryRecord>();
    private static List<GameHistoryRecord> HistoryFor(UserAccount user)
    {
        if (!GuestSessions.TryGetValue(user.SessionId,out var history)) GuestSessions[user.SessionId] = history = new();
        return history;
    }
    private static void Notify(UserAccount user)
    {
        if (ReferenceEquals(CurrentUser,user)) CurrentUserChanged?.Invoke();
    }
    public static bool ValidUsername(string name) => name.Length is >= 3 and <= 50 && name.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
    public static async Task<UsernameStatus> CheckUsernameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!ValidUsername(name.Trim())) return UsernameStatus.Invalid;
        try { return await Store.UsernameExistsAsync(name.Trim(),cancellationToken) ? UsernameStatus.Exists : UsernameStatus.Available; }
        catch (OperationCanceledException) { throw; }
        catch { return UsernameStatus.Error; }
    }

    public static async Task<(bool Success,string Message,UserAccount? User)> RegisterAsync(
        string username,string password,string displayName,string avatar = "👤",bool migrateGuestData = false)
    {
        username = username.Trim(); displayName = displayName.Trim();
        if (!ValidUsername(username)) return (false,"Tên đăng nhập cần 3–50 ký tự, chỉ dùng chữ, số hoặc _.",null);
        if (password.Length is < 8 or > 128 || !password.Any(char.IsLetter) || !password.Any(ch => !char.IsLetter(ch)))
            return (false,"Mật khẩu mới cần 8–128 ký tự, gồm chữ và ít nhất một số hoặc ký tự khác.",null);
        if (displayName.Length == 0) displayName = username;
        if (string.IsNullOrWhiteSpace(avatar)) avatar = "👤";
        if (displayName.Length > 100 || avatar.Length > 20) return (false,"Tên hiển thị tối đa 100 ký tự; ảnh đại diện tối đa 20 ký tự.",null);
        var guest = migrateGuestData && CurrentUser?.IsGuest == true ? CurrentUser : null;
        await Operations.WaitAsync();
        try
        {
            var history = guest != null && !MigratedSessions.Contains(guest.SessionId) ? HistoryFor(guest).ToList() : new List<GameHistoryRecord>();
            string hash = await Task.Run(() => PasswordHasher.Hash(password));
            var user = await Store.RegisterAsync(username,hash,displayName,avatar,history);
            // Store returns only after commit; never clear guest data on a failure.
            if (guest != null)
            {
                HistoryFor(guest).Clear(); ApplyStatistics(guest,Array.Empty<GameHistoryRecord>());
                MigratedSessions.Add(guest.SessionId);
            }
            CurrentUser = user; CurrentUserChanged?.Invoke();
            return (true,"Tạo tài khoản mới thành công!",user);
        }
        catch (DuplicateUsernameException) { return (false,"Tên đăng nhập đã được sử dụng.",null); }
        catch { return (false,"Không thể tạo tài khoản hoặc chuyển lịch sử. Dữ liệu Khách được giữ nguyên; kiểm tra cấu hình SQL Server và thử lại.",null); }
        finally { Operations.Release(); }
    }

    public static async Task<(bool Success,string Message,UserAccount? User)> LoginAsync(string username,string password)
    {
        username = username.Trim();
        if (username.Length is < 1 or > 50 || password.Length == 0)
            return (false,"Vui lòng nhập tên đăng nhập (tối đa 50 ký tự) và mật khẩu.",null);
        await Operations.WaitAsync();
        try
        {
            var account = await Store.FindAccountAsync(username);
            if (account == null) return (false,"Sai tên đăng nhập hoặc mật khẩu!",null);
            var (user,stored) = account.Value;
            var verification = await Task.Run(() => { bool ok = PasswordHasher.Verify(password,stored,out bool upgrade); return (ok,upgrade); });
            if (!verification.ok) return (false,"Sai tên đăng nhập hoặc mật khẩu!",null);
            if (verification.upgrade)
            {
                string hash = await Task.Run(() => PasswordHasher.Hash(password));
                await Store.UpgradePasswordAsync(user.UserId,stored,hash);
            }
            CurrentUser = user; CurrentUserChanged?.Invoke();
            return (true,"Đăng nhập thành công!",user);
        }
        catch { return (false,"Không thể đăng nhập. Kiểm tra kết nối hoặc cấu hình SQL Server rồi thử lại.",null); }
        finally { Operations.Release(); }
    }

    public static void LoginAsGuest(string? customNickname = null,string? customAvatar = null)
    {
        string name = string.IsNullOrWhiteSpace(customNickname) ? GenerateGuestName() : customNickname.Trim();
        string avatar = string.IsNullOrWhiteSpace(customAvatar) ? "👤" : customAvatar;
        if (CurrentUser?.IsGuest != true) CurrentUser = new() { IsGuest = true,Username = "guest",CreatedAt = DateTime.Now };
        CurrentUser.DisplayName = name; CurrentUser.Avatar = avatar;
        ApplyStatistics(CurrentUser,HistoryFor(CurrentUser));
        CurrentUserChanged?.Invoke();
    }
    public static string GenerateGuestName() => $"Khách #{Random.Shared.Next(100,1000)}";
    // Logout ends the session. Pending work keeps its captured owner; next Guest login starts fresh.
    public static void Logout() { CurrentUser = null; CurrentUserChanged?.Invoke(); }

    public static async Task<bool> RecordGameResultAsync(UserAccount? player,Guid gameId,string difficulty,int score,int durationSeconds,int mistakes,bool isWin)
    {
        if (player == null) return false;
        int userId = player.UserId;
        var record = new GameHistoryRecord { GameId = gameId,UserId = userId,Difficulty = difficulty,Score = score,DurationSeconds = durationSeconds,Mistakes = mistakes,IsWin = isWin,PlayedAt = DateTime.Now };
        await Operations.WaitAsync();
        try
        {
            if (player.IsGuest)
            {
                if (MigratedSessions.Contains(player.SessionId)) return false;
                var history = HistoryFor(player);
                if (history.All(h => h.GameId != gameId)) { record.HistoryId = history.Count+1; history.Add(record); }
                ApplyStatistics(player,history);
            }
            else
            {
                await Store.SaveResultAsync(record);
                // Commit is the saving outcome; a subsequent refresh failure cannot undo it.
                try { ApplyStatistics(player,await Store.GetHistoryAsync(userId)); }
                catch { /* History UI reports refresh errors separately. */ }
            }
            Notify(player); return true;
        }
        catch { return false; }
        finally { Operations.Release(); }
    }

    public static async Task<(bool Success,List<GameHistoryRecord> Records)> GetUserHistoryAsync(UserAccount player)
    {
        await Operations.WaitAsync();
        try
        {
            List<GameHistoryRecord> history = player.IsGuest ? HistoryFor(player).OrderByDescending(h => h.PlayedAt).ToList() : await Store.GetHistoryAsync(player.UserId);
            ApplyStatistics(player,history); Notify(player);
            return (true,history);
        }
        catch { return (false,new()); }
        finally { Operations.Release(); }
    }
    public static void ApplyStatistics(UserAccount player,IReadOnlyCollection<GameHistoryRecord> history)
    {
        player.TotalGames = history.Count; player.TotalWins = history.Count(h => h.IsWin);
        player.TotalScore = history.Sum(h => h.Score); player.HighScore = history.Count == 0 ? 0 : history.Max(h => h.Score);
    }
}
