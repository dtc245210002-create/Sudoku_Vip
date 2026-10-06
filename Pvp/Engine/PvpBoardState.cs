using sudokuvip.Pvp.Models;
using sudokuvip.Pvp.Network;

namespace sudokuvip.Pvp.Engine;

// The UI and server apply the same operations; client totals are never authoritative.
public sealed class PvpBoardState
{
    public SudokuModel Model { get; } = new();
    private readonly SudokuEngine _engine = new();
    public long Sequence { get; private set; }
    public int Filled => Model.Score / 10;
    public int TotalEmpty { get; }
    public bool Finished => Model.Mistakes >= 3 || Filled == TotalEmpty;

    public PvpBoardState(PvpBoardData board)
    {
        if (board.Clues.Length != 81 || board.Solution.Length != 81)
            throw new ArgumentException("Invalid PvP board.");
        for (int i = 0; i < 81; i++)
        {
            int r = i / 9, c = i % 9;
            Model.CurrentBoard[r,c] = board.Clues[i];
            Model.SolutionBoard[r,c] = board.Solution[i];
            Model.IsFixed[r,c] = board.Clues[i] != 0;
            if (!Model.IsFixed[r,c]) TotalEmpty++;
        }
    }

    public bool Apply(MsgMovePayload move, out int changedCell)
    {
        changedCell = move.CellIndex;
        if (Finished || move.Sequence != Sequence + 1 || !Enum.IsDefined(move.Operation)) return false;
        if (move.Operation == PvpOperation.Undo)
        {
            var old = _engine.Undo(Model);
            changedCell = old != null ? old.Row * 9 + old.Col : -1;
        }
        else
        {
            if (move.CellIndex is < 0 or >= 81) return false;
            int r = move.CellIndex / 9, c = move.CellIndex % 9;
            if (Model.IsFixed[r,c] || ((move.Operation is PvpOperation.Move or PvpOperation.Note) && (move.Value is < 1 or > 9))) return false;
            switch (move.Operation)
            {
                case PvpOperation.Move: _engine.MakeMove(Model,r,c,move.Value); break;
                case PvpOperation.Erase: _engine.Erase(Model,r,c); break;
                case PvpOperation.Note: _engine.ToggleNote(Model,r,c,move.Value); break;
            }
        }
        Sequence = move.Sequence;
        return true;
    }

    public MsgProgressPayload Progress(string matchId, int cell)
    {
        var status = new int[81];
        for (int i = 0; i < 81; i++)
        {
            int v = Model.CurrentBoard[i/9,i%9];
            status[i] = Model.IsFixed[i/9,i%9] ? 3 : v == 0 ? 0 : v == Model.SolutionBoard[i/9,i%9] ? 1 : 2;
        }
        return new() { MatchId=matchId,Sequence=Sequence,CellIndex=cell,IsCorrect=cell>=0 && cell<81 && status[cell]==1,
            Mistakes=Model.Mistakes,FilledCorrect=Filled,TotalEmpty=TotalEmpty,Score=Model.Score,CellStates=status };
    }
}
