using System;

namespace sudokuvip.Pvp.Models
{
    public class PvpPlayer
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Avatar { get; set; } = "👤";
        public int EloRating { get; set; } = 1200;
        public bool IsReady { get; set; } = false;
        public bool IsBot { get; set; } = false;
    }

    public enum PvpRoomStatus
    {
        Waiting,
        InGame,
        Finished
    }

    public class PvpRoomInfo
    {
        public string RoomId { get; set; } = Guid.NewGuid().ToString("N");
        public string RoomPin { get; set; } = string.Empty;
        public int Difficulty { get; set; } = 1;
        public string HostId { get; set; } = string.Empty;
        public PvpPlayer? Player1 { get; set; }
        public PvpPlayer? Player2 { get; set; }
        public PvpRoomStatus Status { get; set; } = PvpRoomStatus.Waiting;

        public bool IsFull => Player1 != null && Player2 != null;
        public bool CanStart => IsFull && Player1!.IsReady && Player2!.IsReady;
    }

    public class PvpBoardData
    {
        public int[] Clues { get; set; } = new int[81];
        public int[] Solution { get; set; } = new int[81];
        public int TotalEmpty { get; set; }

        public static PvpBoardData FromModel(SudokuModel model)
        {
            var data = new PvpBoardData();
            int empty = 0;
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    int idx = r * 9 + c;
                    data.Solution[idx] = model.SolutionBoard[r, c];
                    if (model.IsFixed[r, c])
                    {
                        data.Clues[idx] = model.CurrentBoard[r, c];
                    }
                    else
                    {
                        data.Clues[idx] = 0;
                        empty++;
                    }
                }
            }
            data.TotalEmpty = empty;
            return data;
        }
    }

    public class PvpMatchStats
    {
        public string PlayerId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int FilledCorrect { get; set; }
        public int Mistakes { get; set; }
        public int TotalEmpty { get; set; }
        public int DurationSeconds { get; set; }
        public int Score { get; set; }
        public bool Surrendered { get; set; }

        public double CompletionPercentage => TotalEmpty > 0
            ? Math.Min(100.0, Math.Round((double)FilledCorrect / TotalEmpty * 100.0, 1))
            : 0;
    }

    public class PvpMatchResult
    {
        public string MatchId { get; set; } = Guid.NewGuid().ToString("N");
        public string WinnerId { get; set; } = string.Empty;
        public bool IsDraw { get; set; }
        public string Reason { get; set; } = string.Empty;
        public PvpPlayer Player1 { get; set; } = new();
        public PvpPlayer Player2 { get; set; } = new();
        public PvpMatchStats P1Stats { get; set; } = new();
        public PvpMatchStats P2Stats { get; set; } = new();
        public int P1EloBefore { get; set; }
        public int P1EloAfter { get; set; }
        public int P2EloBefore { get; set; }
        public int P2EloAfter { get; set; }
        public int EloDelta { get; set; }
    }
}
