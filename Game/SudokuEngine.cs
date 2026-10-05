using System.Diagnostics;

namespace sudokuvip;

public class SudokuEngine
{
    public SudokuModel StartNewGame(int difficultyLevel)
    {
        int target = difficultyLevel switch { 0 => 32, 1 => 42, 2 => 50, 3 => 56, _ => 36 };
        SudokuModel? best = null;
        int bestRemoved = -1;
        var clock = Stopwatch.StartNew();
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var m = new SudokuModel();
            GenerateRandomBoard(m);
            int removed = RemoveCells(m, target, clock);
            if (removed > bestRemoved) { best = m; bestRemoved = removed; }
            if (removed == target || clock.ElapsedMilliseconds >= 2000) break;
        }
        // Fallback is the best verified unique puzzle, possibly with fewer empty cells.
        return best!;
    }

    private static void GenerateRandomBoard(SudokuModel m)
    {
        int[] Shuffle(IEnumerable<int> xs) => xs.OrderBy(_ => Random.Shared.Next()).ToArray();
        var digits = Shuffle(Enumerable.Range(1,9));
        var rows = Shuffle(Enumerable.Range(0,3)).SelectMany(b => Shuffle(Enumerable.Range(0,3)).Select(r => b*3+r)).ToArray();
        var cols = Shuffle(Enumerable.Range(0,3)).SelectMany(b => Shuffle(Enumerable.Range(0,3)).Select(c => b*3+c)).ToArray();
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
            {
                int v = digits[(rows[r]*3+rows[r]/3+cols[c])%9];
                m.CurrentBoard[r,c] = m.SolutionBoard[r,c] = v; m.IsFixed[r,c] = true;
            }
    }

    private static int RemoveCells(SudokuModel m, int target, Stopwatch clock)
    {
        int removed = 0;
        foreach (int i in Enumerable.Range(0,81).OrderBy(_ => Random.Shared.Next()))
        {
            if (removed >= target || clock.ElapsedMilliseconds >= 2000) break;
            int r = i/9, c = i%9, old = m.CurrentBoard[r,c];
            m.CurrentBoard[r,c] = 0;
            if (SudokuSolver.CountSolutions(m.CurrentBoard, 50000) == 1)
            { m.IsFixed[r,c] = false; removed++; }
            else m.CurrentBoard[r,c] = old;
        }
        return removed;
    }

    private static bool CanEdit(SudokuModel m, int r, int c) =>
        m.State == GameState.Playing && r >= 0 && r < 9 && c >= 0 && c < 9 && !m.IsFixed[r,c];
    private static void Snapshot(SudokuModel m, int r, int c) =>
        m.UndoHistory.Push(new(r,c,m.CurrentBoard[r,c],new(m.Notes[r,c]),m.Mistakes));

    public bool MakeMove(SudokuModel m, int r, int c, int value)
    {
        if (!CanEdit(m,r,c) || value < 1 || value > 9) return false;
        if (m.CurrentBoard[r,c] == value) return value == m.SolutionBoard[r,c];
        Snapshot(m,r,c);
        m.CurrentBoard[r,c] = value; m.Notes[r,c].Clear();
        bool correct = value == m.SolutionBoard[r,c];
        if (!correct) m.Mistakes++;
        return correct;
    }
    public bool Erase(SudokuModel m, int r, int c)
    {
        if (!CanEdit(m,r,c) || (m.CurrentBoard[r,c] == 0 && m.Notes[r,c].Count == 0)) return false;
        Snapshot(m,r,c); m.CurrentBoard[r,c] = 0; m.Notes[r,c].Clear(); return true;
    }
    public bool ToggleNote(SudokuModel m, int r, int c, int value)
    {
        if (!CanEdit(m,r,c) || m.CurrentBoard[r,c] != 0 || value < 1 || value > 9) return false;
        Snapshot(m,r,c);
        if (!m.Notes[r,c].Remove(value)) m.Notes[r,c].Add(value);
        return true;
    }
    public MoveHistoryItem? Undo(SudokuModel m)
    {
        if (m.State != GameState.Playing || !m.UndoHistory.TryPop(out var move)) return null;
        m.CurrentBoard[move.Row,move.Col] = move.PreviousValue;
        m.Notes[move.Row,move.Col] = new(move.PreviousNotes);
        m.Mistakes = move.PreviousMistakes;
        // Hints remain consumed and their cell retains the five-point cap, even after undo.
        return move;
    }
    public int GetHint(SudokuModel m, int r, int c)
    {
        if (!CanEdit(m,r,c) || m.HintsLeft == 0 || m.CurrentBoard[r,c] == m.SolutionBoard[r,c]) return -1;
        Snapshot(m,r,c); m.HintsLeft--; m.HintUsed[r,c] = true;
        m.CurrentBoard[r,c] = m.SolutionBoard[r,c]; m.Notes[r,c].Clear();
        return m.CurrentBoard[r,c];
    }
    public bool IsGameWon(SudokuModel m)
    {
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
                if (m.CurrentBoard[r,c] == 0 || m.CurrentBoard[r,c] != m.SolutionBoard[r,c]) return false;
        return SudokuSolver.CountSolutions(m.CurrentBoard) == 1;
    }
    public bool TryFinish(SudokuModel m, bool won)
    {
        if (m.State != GameState.Playing || (won ? !IsGameWon(m) : m.Mistakes < 3)) return false;
        m.State = won ? GameState.Won : GameState.Lost; return true;
    }
    public void TogglePause(SudokuModel m)
    {
        if (m.State == GameState.Playing) m.State = GameState.Paused;
        else if (m.State == GameState.Paused) m.State = GameState.Playing;
    }
}
