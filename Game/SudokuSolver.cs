namespace sudokuvip;

public static class SudokuSolver
{
    // Work on a clone; stop after the second solution. Exhausted budgets reject removals.
    public static int CountSolutions(int[,] input, int nodeBudget = int.MaxValue)
    {
        int size = input.GetLength(0);
        if (size is not (4 or 9) || input.GetLength(1) != size) return 0;
        int boxSize = size == 4 ? 2 : 3;
        int allDigits = (1 << (size + 1)) - 2;
        var board = (int[,])input.Clone();
        var rows = new int[size]; var cols = new int[size]; var boxes = new int[size];
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
            {
                int v = board[r,c];
                if (v < 0 || v > size) return 0;
                if (v == 0) continue;
                int bit = 1 << v, b = r / boxSize * boxSize + c / boxSize;
                if (((rows[r] | cols[c] | boxes[b]) & bit) != 0) return 0;
                rows[r] |= bit; cols[c] |= bit; boxes[b] |= bit;
            }
        int count = 0, nodes = 0;
        bool exhausted = false;
        void Search()
        {
            if (count >= 2 || exhausted) return;
            if (++nodes > nodeBudget) { exhausted = true; return; }
            int br = -1, bc = -1, mask = 0, best = size + 1;
            for (int r = 0; r < size; r++)
                for (int c = 0; c < size; c++)
                    if (board[r,c] == 0)
                    {
                        int m = allDigits & ~(rows[r] | cols[c] | boxes[r/boxSize*boxSize+c/boxSize]);
                        int n = System.Numerics.BitOperations.PopCount((uint)m);
                        if (n == 0) return;
                        if (n < best) { best = n; br = r; bc = c; mask = m; }
                    }
            if (br == -1) { count++; return; }
            int box = br/boxSize*boxSize+bc/boxSize;
            for (int v = 1; v <= size; v++)
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
