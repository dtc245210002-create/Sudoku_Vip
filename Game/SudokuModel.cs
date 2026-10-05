namespace sudokuvip;

public enum GameState { Playing, Paused, Won, Lost }
public enum ResultSaveState { NotStarted, Saving, Saved, Failed }

public class SudokuModel
{
    public Guid GameId { get; } = Guid.NewGuid();
    public GameState State { get; internal set; } = GameState.Playing;
    public ResultSaveState SaveState { get; set; }
    public int Size { get; }
    public int BoxSize { get; }
    public int[,] CurrentBoard { get; set; }
    public int[,] SolutionBoard { get; set; }
    public bool[,] IsFixed { get; set; }
    public bool[,] HintUsed { get; }
    public HashSet<int>[,] Notes { get; }
    public Stack<MoveHistoryItem> UndoHistory { get; } = new();
    public int Mistakes { get; internal set; }
    public int HintsLeft { get; internal set; } = 3;
    public int Score
    {
        get
        {
            int score = 0;
            for (int r = 0; r < Size; r++)
                for (int c = 0; c < Size; c++)
                    if (!IsFixed[r,c] && CurrentBoard[r,c] != 0 && CurrentBoard[r,c] == SolutionBoard[r,c])
                        score += HintUsed[r,c] ? 5 : 10;
            return score;
        }
    }
    public SudokuModel(int size = 9)
    {
        if (size is not (4 or 9)) throw new ArgumentOutOfRangeException(nameof(size));
        Size = size;
        BoxSize = size == 4 ? 2 : 3;
        CurrentBoard = new int[size, size];
        SolutionBoard = new int[size, size];
        IsFixed = new bool[size, size];
        HintUsed = new bool[size, size];
        Notes = new HashSet<int>[size, size];
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++) Notes[r,c] = new();
    }
}

public record MoveHistoryItem(int Row, int Col, int PreviousValue, HashSet<int> PreviousNotes, int PreviousMistakes);
