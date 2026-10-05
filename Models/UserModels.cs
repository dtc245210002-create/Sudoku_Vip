using System;

namespace sudokuvip.Models
{
    public class UserAccount
    {
        public Guid SessionId { get; } = Guid.NewGuid();
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Avatar { get; set; } = "👤";
        public DateTime CreatedAt { get; set; }
        public int HighScore { get; set; }
        public int TotalScore { get; set; }
        public int TotalGames { get; set; }
        public int TotalWins { get; set; }
        public int EloRating { get; set; } = 1200;
        public int PvpGames { get; set; }
        public int PvpWins { get; set; }
        public bool IsGuest { get; set; } = false;

        public double WinRate => TotalGames > 0 ? Math.Round((double)TotalWins / TotalGames * 100, 1) : 0;
        public double PvpWinRate => PvpGames > 0 ? Math.Round((double)PvpWins / PvpGames * 100, 1) : 0;
    }

    public class GameHistoryRecord
    {
        public Guid GameId { get; set; } = Guid.NewGuid();
        public int HistoryId { get; set; }
        public int UserId { get; set; }
        public string Difficulty { get; set; } = string.Empty;
        public int Score { get; set; }
        public int DurationSeconds { get; set; }
        public int Mistakes { get; set; }
        public bool IsWin { get; set; }
        public DateTime PlayedAt { get; set; }

        public string DurationFormatted
        {
            get
            {
                int minutes = DurationSeconds / 60;
                int seconds = DurationSeconds % 60;
                return $"{minutes:D2}:{seconds:D2}";
            }
        }

        public string ResultText => IsWin ? "Chiến thắng" : "Thua cuộc";
        public string ResultColor => IsWin ? "#4ADE80" : "#F87171";
        public string FormattedPlayedAt => PlayedAt.ToString("dd/MM/yyyy HH:mm");
    }
}
