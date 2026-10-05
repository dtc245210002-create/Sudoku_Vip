namespace sudokuvip;

public enum GameState { Playing, Paused, Won, Lost }
public enum ResultSaveState { NotStarted, Saving, Saved, Failed }

public class SudokuModel
{
    public Guid GameId { get; } = Guid.NewGuid();
    public GameState State { get; internal set; } = GameState.Playing;
    public ResultSaveState SaveState { get; set; }
    public int[,] CurrentBoard { get; set; } = new int[9, 9];
    public int[,] SolutionBoard { get; set; } = new int[9, 9];
    public bool[,] IsFixed { get; set; } = new bool[9, 9];
    public bool[,] HintUsed { get; } = new bool[9, 9];
    public HashSet<int>[,] Notes { get; } = new HashSet<int>[9, 9];
    public Stack<MoveHistoryItem> UndoHistory { get; } = new();
    public int Mistakes { get; internal set; }
    public int HintsLeft { get; internal set; } = 3;
    public int Score
    {
        get
        {
            int score = 0;
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (!IsFixed[r,c] && CurrentBoard[r,c] != 0 && CurrentBoard[r,c] == SolutionBoard[r,c])
                        score += HintUsed[r,c] ? 5 : 10;
            return score;
        }
    }
    public SudokuModel()
    {
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++) Notes[r,c] = new();
    }
}

public record MoveHistoryItem(int Row, int Col, int PreviousValue, HashSet<int> PreviousNotes, int PreviousMistakes);
