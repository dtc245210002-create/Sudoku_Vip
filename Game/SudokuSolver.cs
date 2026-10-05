namespace sudokuvip;

public static class SudokuSolver
{
    // Work on a clone; stop after the second solution. Exhausted budgets reject removals.
    public static int CountSolutions(int[,] input, int nodeBudget = int.MaxValue)
    {
        if (input.GetLength(0) != 9 || input.GetLength(1) != 9) return 0;
        var board = (int[,])input.Clone();
        var rows = new int[9]; var cols = new int[9]; var boxes = new int[9];
        for (int r = 0; r < 9; r++)
            for (int c = 0; c < 9; c++)
            {
                int v = board[r,c];
                if (v < 0 || v > 9) return 0;
                if (v == 0) continue;
                int bit = 1 << v, b = r / 3 * 3 + c / 3;
                if (((rows[r] | cols[c] | boxes[b]) & bit) != 0) return 0;
                rows[r] |= bit; cols[c] |= bit; boxes[b] |= bit;
            }
        int count = 0, nodes = 0;
        bool exhausted = false;
        void Search()
        {
            if (count >= 2 || exhausted) return;
            if (++nodes > nodeBudget) { exhausted = true; return; }
            int br = -1, bc = -1, mask = 0, best = 10;
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (board[r,c] == 0)
                    {
                        int m = 0x3FE & ~(rows[r] | cols[c] | boxes[r/3*3+c/3]);
                        int n = System.Numerics.BitOperations.PopCount((uint)m);
                        if (n == 0) return;
                        if (n < best) { best = n; br = r; bc = c; mask = m; }
                    }
            if (br == -1) { count++; return; }
            int box = br/3*3+bc/3;
            for (int v = 1; v <= 9; v++)
            {
                int bit = 1 << v;
                if ((mask & bit) == 0) continue;
                board[br,bc] = v; rows[br] |= bit; cols[bc] |= bit; boxes[box] |= bit;
                Search();
                board[br,bc] = 0; rows[br] &= ~bit; cols[bc] &= ~bit; boxes[box] &= ~bit;
                if (count >= 2 || exhausted) break;
            }
        }
        Search();
        return exhausted ? 2 : count;
    }
}
