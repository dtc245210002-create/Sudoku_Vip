using System.Reflection;
using System.Windows;
using sudokuvip;
using sudokuvip.Pvp;
using sudokuvip.Pvp.Engine;
using sudokuvip.Pvp.Models;
using sudokuvip.Pvp.Network;
using sudokuvip.Pvp.Views;
using sudokuvip.Services;

internal static class PvpIntegrationTests
{
    private static Action<bool,string> Check = null!;
    public static void Run(Action<bool,string> check)
    {
        Check=check;
        StateAndWpf();
        WpfLifecycle();
        Task.Run(Network).GetAwaiter().GetResult();
        Console.WriteLine("PASS: PvP WPF scoring, handshake, two TCP clients, queue/room/bot, server validation, undo/erase sync, KO/win/forfeit, reconnect and captured-owner history.");
    }
    private static object Field(object target,string name) => target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    private static void Call(object target,string name,params object[] args) => target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(target,args);
    private static void StateAndWpf()
    {
        var model=new SudokuEngine().StartNewGame(0);
        var board=PvpBoardData.FromModel(model);
        int cell=Array.FindIndex(board.Clues,v=>v==0),v=board.Solution[cell];
        var state=new PvpBoardState(board);
        bool Move(PvpOperation operation,int value=0) => state.Apply(new() {Sequence=state.Sequence+1,Operation=operation,CellIndex=cell,Value=value},out _);
        Check(Move(PvpOperation.Move,v)&&state.Filled==1&&state.Model.Score==10,"PvP correct contributes once");
        Check(Move(PvpOperation.Move,v)&&state.Model.UndoHistory.Count==1,"PvP repeat no undo entry");
        Check(Move(PvpOperation.Erase)&&state.Filled==0&&state.Model.Score==0,"PvP erase removes score");
        Move(PvpOperation.Move,v); Move(PvpOperation.Move,v%9+1);
        Check(state.Filled==0&&state.Model.Score==0&&state.Model.Mistakes==1,"correct overwritten with wrong removes progress");
        Move(PvpOperation.Undo);
        Check(state.Filled==1&&state.Model.Score==10&&state.Model.Mistakes==0,"PvP undo restores score and mistakes");
        Move(PvpOperation.Erase); Move(PvpOperation.Note,2); Move(PvpOperation.Undo);
        Check(state.Model.Notes[cell/9,cell%9].Count==0,"notes and undo share operation history");
        Check(!Move(PvpOperation.Move,10)&&!state.Apply(new(){Sequence=1,CellIndex=cell,Value=v},out _),"invalid value and replay rejected");
        int fixedCell=Array.FindIndex(board.Clues,x=>x!=0);
        Check(!state.Apply(new(){Sequence=state.Sequence+1,CellIndex=fixedCell,Value=1},out _),"clue edits rejected");

        var arena=new PvpArenaWindow(new(){MatchId="wpf-test",Board=board});
        Call(arena,"ApplyNumber",v); Check((int)Field(arena,"_myFilled")==1&&(int)Field(arena,"_myScore")==10,"actual arena correct");
        Call(arena,"BtnErase_Click",arena,new RoutedEventArgs()); Check((int)Field(arena,"_myScore")==0,"actual arena erase");
        Call(arena,"ApplyNumber",v); Call(arena,"ApplyNumber",v%9+1);
        Check((int)Field(arena,"_myFilled")==0&&(int)Field(arena,"_myScore")==0,"actual arena overwrite");
        Call(arena,"BtnUndo_Click",arena,new RoutedEventArgs());
        Check((int)Field(arena,"_myScore")==10&&(int)Field(arena,"_myMistakes")==0,"actual arena undo");
        arena.Close();
        var result=new PvpMatchResult {Player1=new(){Id="server-one"},Player2=new(){Id="server-two"},WinnerId="server-two",
            P1EloBefore=1200,P1EloAfter=1184,P2EloBefore=1200,P2EloAfter=1216};
        var dialog=new PvpResultDialog(result,"server-two");
        Check(((System.Windows.Controls.TextBlock)Field(dialog,"txtTitle")).Text.Contains("CHIẾN THẮNG")&&
            ((System.Windows.Controls.TextBlock)Field(dialog,"txtP1EloChange")).Text.Contains("+16"),"actual result dialog uses canonical P2 identity and winning Elo");
        dialog.Close();
        dialog=new PvpResultDialog(result,"server-one");
        Check(((System.Windows.Controls.TextBlock)Field(dialog,"txtTitle")).Text.Contains("THẤT BẠI")&&
            ((System.Windows.Controls.TextBlock)Field(dialog,"txtP1EloChange")).Text.Contains("-16"),"actual result dialog uses losing P1 Elo");
        dialog.Close();
    }
    private static T Pump<T>(Task<T> task)
    {
        var frame=new System.Windows.Threading.DispatcherFrame();
        var clock=System.Diagnostics.Stopwatch.StartNew();
        var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(10)};
        timer.Tick+=(_,_)=>{if(task.IsCompleted||clock.ElapsedMilliseconds>10000) frame.Continue=false;};
        timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame); timer.Stop();
        if (!task.IsCompleted) throw new Exception("WPF networking dispatcher timeout");
        return task.GetAwaiter().GetResult();
    }
    private static void WpfLifecycle()
    {
        AuthService.Store=new FakeStore(); AuthService.Logout(); AuthService.LoginAsGuest();
        using var server=new PvpServer(); Check(server.Start(0),"WPF lifecycle loopback server");
        var manager=PvpManager.Instance;
        Check(Pump(manager.EnsureConnectedAsync("127.0.0.1",server.Port)),"WPF manager handshake");
        using var opponent=new Peer(); Pump(Task.Run(()=>opponent.Connect(server.Port,"WPF opponent")));
        var started=new TaskCompletionSource<MsgStartMatchPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        Action<MsgStartMatchPayload> handler=m=>started.TrySetResult(m);
        manager.OnMatchStarted+=handler;
        _=manager.JoinQueue(0); _=opponent.Client.JoinQueue(0);
        var match=Pump(started.Task); Pump(opponent.Wait(PvpMessageType.StartMatch));
        manager.OnMatchStarted-=handler;
        var arena=new PvpArenaWindow(match);
        arena.Close();
        var result=Pump(opponent.Wait(PvpMessageType.MatchOver)).GetPayload<PvpMatchResult>()!;
        Check(result.P1Stats.Surrendered||result.P2Stats.Surrendered,"closing actual arena forfeits match");
        Check(!((System.Windows.Threading.DispatcherTimer)Field(arena,"_timer")).IsEnabled,"arena close stops timer");
        var lobby=new PvpLobbyWindow();
        var preview=Environment.GetEnvironmentVariable("SUDOKU_PREVIEW_DIR");
        if (preview!=null)
        {
            var root=(FrameworkElement)lobby.Content;
            root.Measure(new Size(860,660));root.Arrange(new Rect(0,0,860,660));root.UpdateLayout();
            var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(860,660,96,96,System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            System.IO.Directory.CreateDirectory(preview);
            using var output=System.IO.File.Create(System.IO.Path.Combine(preview,"wpf-pvp-lobby.png"));encoder.Save(output);
        }
        lobby.Close();
        Check(!manager.Client.IsConnected&&!((System.Windows.Threading.DispatcherTimer)Field(lobby,"_searchTimer")).IsEnabled,"closing actual lobby disconnects and stops search timer");
    }
    private sealed class Peer : IDisposable
    {
        public PvpClient Client { get; } = new();
        private readonly List<PvpMessage> _messages = new();
        private string _error="";
        public Peer() { Client.OnMessageReceived += message => { lock (_messages) _messages.Add(message); }; Client.OnError+=error=>_error=error; }
        public async Task<PvpMessage> Wait(PvpMessageType type,Func<PvpMessage,bool>? predicate=null)
        {
            var timeout=System.Diagnostics.Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds<6000)
            {
                lock (_messages)
                {
                    int i=_messages.FindIndex(m=>m.Type==type&&(predicate==null||predicate(m)));
                    if (i>=0) { var message=_messages[i]; _messages.RemoveAt(i); return message; }
                }
                await Task.Delay(10);
            }
            throw new Exception("Timed out waiting for "+type);
        }
        public bool Has(PvpMessageType type) { lock (_messages) return _messages.Any(m=>m.Type==type); }
        public async Task<PvpPlayer> Connect(int port,string name)
        {
            Check(await Client.ConnectAsync("127.0.0.1",port),"TCP connected: "+_error);
            var local=new PvpPlayer {DisplayName=name};
            await Client.SendHello(local);
            var canonical=(await Wait(PvpMessageType.HelloAck)).GetPayload<PvpPlayer>()!;
            Check(canonical.Id!=local.Id&&canonical.Id==Client.CurrentPlayer?.Id,"HelloAck canonical ID");
            return canonical;
        }
        public void Dispose()=>Client.Dispose();
    }
    private static async Task<(MsgStartMatchPayload A,MsgStartMatchPayload B)> Queue(Peer a,Peer b)
    {
        await a.Client.JoinQueue(0); await b.Client.JoinQueue(0);
        var first=(await a.Wait(PvpMessageType.StartMatch)).GetPayload<MsgStartMatchPayload>()!;
        var second=(await b.Wait(PvpMessageType.StartMatch)).GetPayload<MsgStartMatchPayload>()!;
        Check(first.MatchId==second.MatchId&&first.Board.Clues.SequenceEqual(second.Board.Clues),"two clients share match and puzzle");
        return (first,second);
    }
    private static async Task Network()
    {
        using var server=new PvpServer(); Check(server.Start(0),"ephemeral loopback server starts");
        using var a=new Peer(); using var b=new Peer();
        var pa=await a.Connect(server.Port,"A"); var pb=await b.Connect(server.Port,"B");
        var (match,other)=await Queue(a,b);
        Check(match.Opponent.Id==pb.Id&&other.Opponent.Id==pa.Id,"opponents use canonical IDs");
        int cell=Array.FindIndex(match.Board.Clues,v=>v==0),value=match.Board.Solution[cell]; long sequence=0;
        await a.Client.SendProgress(match.MatchId,cell,true,0,999,1,9999); // Legacy forged totals must be ignored.
        await a.Client.SendAsync(PvpMessageType.PlayerMove,new MsgMovePayload {MatchId="stale-match",Sequence=1,CellIndex=cell,Value=value});
        async Task<MsgProgressPayload> Move(PvpOperation operation,int v=0,int? index=null)
        {
            await a.Client.SendAsync(PvpMessageType.PlayerMove,new MsgMovePayload {MatchId=match.MatchId,Sequence=++sequence,Operation=operation,CellIndex=index??cell,Value=v});
            var local=(await a.Wait(PvpMessageType.LocalProgress,m=>m.GetPayload<MsgProgressPayload>()?.Sequence==sequence)).GetPayload<MsgProgressPayload>()!;
            var remote=(await b.Wait(PvpMessageType.OpponentProgress,m=>m.GetPayload<MsgProgressPayload>()?.Sequence==sequence)).GetPayload<MsgProgressPayload>()!;
            Check(local.Score==remote.Score&&local.FilledCorrect==remote.FilledCorrect&&local.CellStates.SequenceEqual(remote.CellStates),"server sync agrees on both clients");
            return remote;
        }
        var progress=await Move(PvpOperation.Move,value);
        Check(progress.Score==10&&progress.FilledCorrect==1&&!a.Has(PvpMessageType.MatchOver),"forged totals cannot win");
        progress=await Move(PvpOperation.Erase); Check(progress.Score==0&&progress.CellStates[cell]==0,"erase synchronizes empty mini-cell");
        await Move(PvpOperation.Move,value); progress=await Move(PvpOperation.Move,value%9+1);
        Check(progress.Score==0&&progress.Mistakes==1,"server recomputes overwritten contribution");
        progress=await Move(PvpOperation.Undo); Check(progress.Score==10&&progress.Mistakes==0&&progress.CellStates[cell]==1,"undo synchronizes restored cell");
        await a.Client.SendAsync(PvpMessageType.PlayerMove,new MsgMovePayload {MatchId=match.MatchId,Sequence=sequence,CellIndex=cell,Value=1});
        await a.Wait(PvpMessageType.Error);
        for (int i=0;i<81;i++) if (match.Board.Clues[i]==0 && i!=cell) await Move(PvpOperation.Move,match.Board.Solution[i],i);
        var win=(await a.Wait(PvpMessageType.MatchOver)).GetPayload<PvpMatchResult>()!;
        var loss=(await b.Wait(PvpMessageType.MatchOver)).GetPayload<PvpMatchResult>()!;
        Check(win.MatchId==loss.MatchId&&win.WinnerId==pa.Id&&win.P1Stats.FilledCorrect+win.P2Stats.FilledCorrect==match.Board.TotalEmpty,"only completed board wins once");

        (match,other)=await Queue(a,b); cell=Array.FindIndex(match.Board.Clues,v=>v==0); value=match.Board.Solution[cell]; sequence=0;
        await Move(PvpOperation.Move,value%9+1); await Move(PvpOperation.Move,(value+1)%9+1); await Move(PvpOperation.Move,value%9+1);
        var ko=(await a.Wait(PvpMessageType.MatchOver)).GetPayload<PvpMatchResult>()!; await b.Wait(PvpMessageType.MatchOver);
        Check(ko.WinnerId==pb.Id&&(ko.P1Stats.Mistakes==3||ko.P2Stats.Mistakes==3),"server ends at three mistakes");

        await a.Client.CreateRoom(1); var room=(await a.Wait(PvpMessageType.RoomUpdate)).GetPayload<PvpRoomInfo>()!;
        await b.Client.JoinRoom(room.RoomPin); await b.Wait(PvpMessageType.RoomUpdate);
        await a.Client.ToggleReady(true); await b.Client.ToggleReady(true);
        match=(await a.Wait(PvpMessageType.StartMatch)).GetPayload<MsgStartMatchPayload>()!; await b.Wait(PvpMessageType.StartMatch);
        await a.Client.StartBotMatch(0); await a.Wait(PvpMessageType.Error);
        await b.Client.Surrender("stale-match");
        await b.Client.Surrender(match.MatchId);
        win=(await a.Wait(PvpMessageType.MatchOver)).GetPayload<PvpMatchResult>()!; await b.Wait(PvpMessageType.MatchOver);
        Check(win.WinnerId==pa.Id,"room readiness starts one match; surrender validates match ID");
        await a.Client.LeaveRoom(); await b.Client.LeaveRoom();
        await a.Client.StartBotMatch(0); match=(await a.Wait(PvpMessageType.StartMatch)).GetPayload<MsgStartMatchPayload>()!;
        Check(match.Opponent.IsBot,"bot match starts");
        await a.Client.Surrender(match.MatchId); await a.Wait(PvpMessageType.MatchOver);

        (match,other)=await Queue(a,b); b.Client.Disconnect();
        win=(await a.Wait(PvpMessageType.MatchOver)).GetPayload<PvpMatchResult>()!;
        Check(win.WinnerId==pa.Id,"disconnect forfeits active match");
        var reconnected=await b.Connect(server.Port,"B2"); Check(reconnected.Id!=pb.Id,"reconnect has fresh identity");
        await b.Client.JoinQueue(0); b.Client.Disconnect();
        await b.Connect(server.Port,"B3");
        (match,other)=await Queue(a,b); Check(match.Opponent.DisplayName=="B3","closed queued client cannot be matched");
        await b.Client.Surrender(match.MatchId); await a.Wait(PvpMessageType.MatchOver); await b.Wait(PvpMessageType.MatchOver);
        await PersistenceAndIdentity(server.Port);
    }
    private static async Task PersistenceAndIdentity(int port)
    {
        AuthService.Store=new FakeStore(); AuthService.Logout(); AuthService.LoginAsGuest("owner");
        var owner=AuthService.CurrentUser!;
        using var manager=new PvpManager(); using var opponent=new Peer(); await opponent.Connect(port,"opponent");
        Check(await manager.EnsureConnectedAsync("127.0.0.1",port)&&manager.LocalPlayer?.Id==manager.Client.CurrentPlayer?.Id,"manager waits for canonical handshake");
        var started=new TaskCompletionSource<MsgStartMatchPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed=new TaskCompletionSource<PvpMatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.OnMatchStarted+=m=>started.TrySetResult(m); manager.OnMatchOver+=r=>completed.TrySetResult(r);
        await manager.JoinQueue(0); await opponent.Client.JoinQueue(0);
        var match=await started.Task.WaitAsync(TimeSpan.FromSeconds(6)); await opponent.Wait(PvpMessageType.StartMatch);
        string canonical=manager.LocalPlayer!.Id;
        AuthService.Logout(); AuthService.LoginAsGuest("new owner"); var next=AuthService.CurrentUser!;
        await opponent.Client.Surrender(match.MatchId);
        var result=await completed.Task.WaitAsync(TimeSpan.FromSeconds(6));
        var history=await AuthService.GetUserHistoryAsync(owner);
        Check(history.Success&&history.Records.Count==1&&history.Records[0].IsPvp&&owner.PvpWins==1&&owner.EloRating>1200,"result saves to captured guest with Elo/history");
        Check(next.PvpGames==0&&!manager.IsReady,"changed account cannot inherit old result or send old identity");
        var handler=typeof(PvpManager).GetMethod("HandleServerMessage",BindingFlags.NonPublic|BindingFlags.Instance)!;
        handler.Invoke(manager,[PvpMessage.Create(PvpMessageType.MatchOver,result)]);
        await AuthService.RecordPvpResultAsync(owner,result,canonical);
        Check((await AuthService.GetUserHistoryAsync(owner)).Records.Count==1&&owner.PvpGames==1,"repeat result is idempotent in manager and history");
        Check(await manager.EnsureConnectedAsync("127.0.0.1",port)&&manager.LocalPlayer!.DisplayName=="new owner"&&manager.LocalPlayer.Id!=canonical,"new owner reconnects with fresh profile");
        var store=(FakeStore)AuthService.Store;
        var account=store.AddAccount("registered",PasswordHasher.Hash("Password123"));
        store.FailAll=true;
        Check(!await AuthService.RecordPvpResultAsync(account,result,canonical),"failed SQL store reports unsaved result");
        store.FailAll=false;
        Check(await AuthService.RecordPvpResultAsync(account,result,canonical)&&await AuthService.RecordPvpResultAsync(account,result,canonical),"registered result retry succeeds");
        var reloaded=new sudokuvip.Models.UserAccount {UserId=account.UserId};
        Check((await AuthService.GetUserHistoryAsync(reloaded)).Records.Count==1&&reloaded.PvpGames==1&&reloaded.EloRating==owner.EloRating,"registered history restores PvP statistics and Elo");
        var newer=new PvpMatchResult {MatchId=Guid.NewGuid().ToString("N"),Player1=new(){Id=canonical},WinnerId=canonical,
            P1EloAfter=1300,FinishedAtUtc=result.FinishedAtUtc.AddMinutes(1)};
        await AuthService.RecordPvpResultAsync(account,newer,canonical);
        await AuthService.RecordPvpResultAsync(account,result,canonical);
        Check(account.EloRating==1300&&account.PvpGames==2,"retrying older result cannot regress Elo");
        AuthService.Logout();AuthService.LoginAsGuest("migrating guest");var guest=AuthService.CurrentUser!;
        await AuthService.RecordPvpResultAsync(guest,newer,canonical);
        store.FailRegister=true;
        Check(!(await AuthService.RegisterAsync("pvpguest","Password123","PvP Guest",migrateGuestData:true)).Success&&guest.PvpGames==1,"failed guest migration preserves PvP history");
        store.FailRegister=false;
        var registered=await AuthService.RegisterAsync("pvpguest","Password123","PvP Guest",migrateGuestData:true);
        Check(registered.Success&&registered.User!.EloRating==1300&&registered.User.PvpWins==1&&guest.PvpGames==0,"explicit guest migration transfers PvP Elo/history after commit");
        Check(!await AuthService.RecordPvpResultAsync(guest,newer,canonical),"sealed guest cannot receive late PvP results");
    }
}
