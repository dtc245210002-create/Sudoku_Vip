using Microsoft.Data.SqlClient;
using sudokuvip.Database;
using sudokuvip.Models;
using System.Data;

namespace sudokuvip.Services;

public sealed class SqlAccountStore : IAccountStore
{
    private static SqlCommand Command(string sql,SqlConnection conn,SqlTransaction? transaction = null)
        => new(sql,conn,transaction) { CommandTimeout = 10 };
    public async Task<bool> UsernameExistsAsync(string username,CancellationToken cancellationToken)
    {
        using var conn = DatabaseHelper.GetConnection(); await conn.OpenAsync(cancellationToken);
        using var cmd = Command("SELECT COUNT(1) FROM dbo.Users WHERE LOWER(Username)=LOWER(@Username)",conn);
        cmd.Parameters.Add("@Username",SqlDbType.NVarChar,50).Value = username;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }
    public async Task<UserAccount> RegisterAsync(string username,string hash,string displayName,string avatar,IReadOnlyList<GameHistoryRecord> history)
    {
        using var conn = DatabaseHelper.GetConnection(); await conn.OpenAsync();
        using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        try
        {
            using (var check = Command("SELECT COUNT(1) FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE LOWER(Username)=LOWER(@Username)",conn,tx))
            {
                check.Parameters.Add("@Username",SqlDbType.NVarChar,50).Value = username;
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) > 0) throw new DuplicateUsernameException();
            }
            using var cmd = Command(@"INSERT INTO dbo.Users (Username,PasswordHash,DisplayName,Avatar)
                OUTPUT INSERTED.UserId,INSERTED.CreatedAt VALUES (@Username,@Hash,@DisplayName,@Avatar)",conn,tx);
            cmd.Parameters.Add("@Username",SqlDbType.NVarChar,50).Value = username;
            cmd.Parameters.Add("@Hash",SqlDbType.NVarChar,256).Value = hash;
            cmd.Parameters.Add("@DisplayName",SqlDbType.NVarChar,100).Value = displayName;
            cmd.Parameters.Add("@Avatar",SqlDbType.NVarChar,20).Value = avatar;
            var user = new UserAccount { Username = username,DisplayName = displayName,Avatar = avatar };
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) throw new InvalidOperationException();
                user.UserId = reader.GetInt32(0); user.CreatedAt = reader.GetDateTime(1);
            }
            foreach (var record in history) await InsertHistoryAsync(conn,tx,record,user.UserId);
            await RecalculateStatisticsAsync(conn,tx,user.UserId);
            await tx.CommitAsync();
            AuthService.ApplyStatistics(user,history.ToList());
            return user;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627) { throw new DuplicateUsernameException(); }
    }
    public async Task<(UserAccount User,string Hash)?> FindAccountAsync(string username)
    {
        using var conn = DatabaseHelper.GetConnection(); await conn.OpenAsync();
        using var cmd = Command(@"SELECT UserId,Username,DisplayName,ISNULL(Avatar,N'👤'),CreatedAt,
            HighScore,TotalScore,TotalGames,TotalWins,PasswordHash FROM dbo.Users WHERE LOWER(Username)=LOWER(@Username)",conn);
        cmd.Parameters.Add("@Username",SqlDbType.NVarChar,50).Value = username;
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return (new UserAccount { UserId=reader.GetInt32(0),Username=reader.GetString(1),DisplayName=reader.GetString(2),Avatar=reader.GetString(3),CreatedAt=reader.GetDateTime(4),
            HighScore=reader.GetInt32(5),TotalScore=reader.GetInt32(6),TotalGames=reader.GetInt32(7),TotalWins=reader.GetInt32(8) },reader.GetString(9));
    }
    public async Task UpgradePasswordAsync(int userId,string oldHash,string newHash)
    {
        using var conn = DatabaseHelper.GetConnection(); await conn.OpenAsync();
        using var cmd = Command("UPDATE dbo.Users SET PasswordHash=@NewHash WHERE UserId=@UserId AND PasswordHash=@OldHash",conn);
        cmd.Parameters.Add("@UserId",SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@NewHash",SqlDbType.NVarChar,256).Value = newHash;
        cmd.Parameters.Add("@OldHash",SqlDbType.NVarChar,256).Value = oldHash;
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task SaveResultAsync(GameHistoryRecord record)
    {
        using var conn = DatabaseHelper.GetConnection(); await conn.OpenAsync();
        using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
        await LockAccountAsync(conn,tx,record.UserId);
        using var check = Command("SELECT UserId FROM dbo.GameHistory WITH (UPDLOCK,HOLDLOCK) WHERE GameId=@GameId",conn,tx);
        check.Parameters.Add("@GameId",SqlDbType.UniqueIdentifier).Value = record.GameId;
        object? existing = await check.ExecuteScalarAsync();
        if (existing == null) await InsertHistoryAsync(conn,tx,record,record.UserId);
        else if (Convert.ToInt32(existing) != record.UserId) throw new InvalidOperationException("Game identity mismatch.");
        await RecalculateStatisticsAsync(conn,tx,record.UserId);
        await tx.CommitAsync();
    }
    private static async Task InsertHistoryAsync(SqlConnection conn,SqlTransaction tx,GameHistoryRecord record,int userId)
    {
        using var cmd = Command(@"INSERT INTO dbo.GameHistory (GameId,UserId,Difficulty,Score,DurationSeconds,Mistakes,IsWin,PlayedAt)
            VALUES (@GameId,@UserId,@Difficulty,@Score,@Duration,@Mistakes,@Win,@PlayedAt)",conn,tx);
        cmd.Parameters.Add("@GameId",SqlDbType.UniqueIdentifier).Value = record.GameId;
        cmd.Parameters.Add("@UserId",SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@Difficulty",SqlDbType.NVarChar,30).Value = record.Difficulty;
        cmd.Parameters.Add("@Score",SqlDbType.Int).Value = record.Score;
        cmd.Parameters.Add("@Duration",SqlDbType.Int).Value = record.DurationSeconds;
        cmd.Parameters.Add("@Mistakes",SqlDbType.Int).Value = record.Mistakes;
        cmd.Parameters.Add("@Win",SqlDbType.Bit).Value = record.IsWin;
        cmd.Parameters.Add("@PlayedAt",SqlDbType.DateTime2).Value = record.PlayedAt;
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task RecalculateStatisticsAsync(SqlConnection conn,SqlTransaction tx,int userId)
    {
        await LockAccountAsync(conn,tx,userId);
        using var cmd = Command(@"UPDATE u SET TotalGames=h.Games,TotalWins=h.Wins,TotalScore=h.Score,HighScore=h.High
            FROM dbo.Users u CROSS APPLY (SELECT COUNT(*) Games,COALESCE(SUM(CASE WHEN IsWin=1 THEN 1 ELSE 0 END),0) Wins,
            COALESCE(SUM(Score),0) Score,COALESCE(MAX(Score),0) High FROM dbo.GameHistory WHERE UserId=@UserId) h WHERE u.UserId=@UserId",conn,tx);
        cmd.Parameters.Add("@UserId",SqlDbType.Int).Value = userId;
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task LockAccountAsync(SqlConnection conn,SqlTransaction tx,int userId)
    {
        using var gate = Command("SELECT UserId FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE UserId=@UserId",conn,tx);
        gate.Parameters.Add("@UserId",SqlDbType.Int).Value = userId;
        if (await gate.ExecuteScalarAsync() == null) throw new InvalidOperationException("Account missing.");
    }
    public async Task<List<GameHistoryRecord>> GetHistoryAsync(int userId)
    {
        using var conn = DatabaseHelper.GetConnection(); await conn.OpenAsync();
        using var cmd = Command(@"SELECT HistoryId,UserId,Difficulty,Score,DurationSeconds,Mistakes,IsWin,PlayedAt,GameId
            FROM dbo.GameHistory WHERE UserId=@UserId ORDER BY PlayedAt DESC",conn);
        cmd.Parameters.Add("@UserId",SqlDbType.Int).Value = userId;
        using var reader = await cmd.ExecuteReaderAsync();
        var history = new List<GameHistoryRecord>();
        while (await reader.ReadAsync()) history.Add(new() { HistoryId=reader.GetInt32(0),UserId=reader.GetInt32(1),Difficulty=reader.GetString(2),Score=reader.GetInt32(3),
            DurationSeconds=reader.GetInt32(4),Mistakes=reader.GetInt32(5),IsWin=reader.GetBoolean(6),PlayedAt=reader.GetDateTime(7),GameId=reader.GetGuid(8) });
        return history;
    }
}
