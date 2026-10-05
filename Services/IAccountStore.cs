using sudokuvip.Models;
namespace sudokuvip.Services;

public sealed class DuplicateUsernameException : Exception { }
public interface IAccountStore
{
    Task<bool> UsernameExistsAsync(string username,CancellationToken cancellationToken);
    Task<UserAccount> RegisterAsync(string username,string hash,string displayName,string avatar,IReadOnlyList<GameHistoryRecord> history);
    Task<(UserAccount User,string Hash)?> FindAccountAsync(string username);
    Task UpgradePasswordAsync(int userId,string oldHash,string newHash);
    Task SaveResultAsync(GameHistoryRecord record);
    Task<List<GameHistoryRecord>> GetHistoryAsync(int userId);
}
