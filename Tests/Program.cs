using sudokuvip;
using sudokuvip.Models;
using sudokuvip.Services;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

internal static class Program
{
    private static int _assertions;
    private static readonly SudokuEngine Engine = new();
    private static void Check(bool condition,string description)
    {
        _assertions++;
        if (!condition) throw new Exception(description);
    }
    [STAThread]
    private static int Main()
    {
        try
        {
            GameTests(); GeneratorTests(); AuthTests().GetAwaiter().GetResult(); WpfTests();
            PvpTests();
            Console.WriteLine($"PASS: {_assertions} assertions; no real database accessed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FAIL: {ex.GetType().Name}: {ex.Message}"); return 1; }
    }
    private static SudokuModel Model()
    {
        var m = Engine.StartNewGame(0);
        m.CurrentBoard = (int[,])m.SolutionBoard.Clone();
        for (int r=0;r<9;r++) for (int c=0;c<9;c++) m.IsFixed[r,c] = true;
        m.CurrentBoard[0,0] = 0; m.IsFixed[0,0] = false;
        m.CurrentBoard[0,1] = 0; m.IsFixed[0,1] = false;
        return m;
    }
    private static void GameTests()
    {
        var m = Model(); int v = m.SolutionBoard[0,0];
        Engine.MakeMove(m,0,0,v); Check(m.Score == 10,"correct value scores ten");
        Engine.MakeMove(m,0,0,v); Check(m.Score == 10 && m.UndoHistory.Count == 1,"repeat is a no-op");
        Engine.Erase(m,0,0); Check(m.Score == 0,"erase removes contribution");
        Engine.Undo(m); Check(m.Score == 10 && m.CurrentBoard[0,0] == v,"undo erase restores points");
        Engine.Erase(m,0,0); Engine.MakeMove(m,0,0,v); Check(m.Score == 10,"correct erase refill cannot farm");
        Engine.MakeMove(m,0,0,v%9+1); Check(m.Score == 0 && m.Mistakes == 1,"wrong correction removes contribution");
        Engine.Undo(m); Check(m.Score == 10 && m.Mistakes == 0,"undo restores correct data and mistakes");
        m = Model(); Engine.ToggleNote(m,0,0,1); Engine.GetHint(m,0,0);
        Check(m.Score == 5 && m.HintsLeft == 2 && m.Notes[0,0].Count == 0,"hint clears notes and contributes five");
        Engine.Undo(m); Check(m.Score == 0 && m.HintsLeft == 2 && m.Notes[0,0].Contains(1),"undo hint restores data without refund");
        Engine.MakeMove(m,0,0,m.SolutionBoard[0,0]); Check(m.Score == 5,"revealed answer remains capped at five");
        Engine.Erase(m,0,0); Engine.GetHint(m,0,0); Check(m.Score == 5 && m.HintsLeft == 1,"repeated hint still costs a use");
        Engine.TogglePause(m); int count = m.UndoHistory.Count;
        Engine.MakeMove(m,0,1,m.SolutionBoard[0,1]); Engine.Erase(m,0,0); Engine.GetHint(m,0,1); Engine.Undo(m); Engine.ToggleNote(m,0,1,2);
        Check(m.State == GameState.Paused && m.UndoHistory.Count == count && m.CurrentBoard[0,1] == 0,"all edits blocked while paused");
        Engine.TogglePause(m); Engine.MakeMove(m,0,1,m.SolutionBoard[0,1]);
        Check(Engine.TryFinish(m,true),"finish complete valid board");
        int score = m.Score; count = m.UndoHistory.Count;
        Check(!Engine.TryFinish(m,true),"finish is one-shot");
        Engine.Erase(m,0,0); Engine.MakeMove(m,0,0,1); Engine.Undo(m); Engine.GetHint(m,0,0); Engine.TogglePause(m);
        Check(m.State == GameState.Won && m.Score == score && m.UndoHistory.Count == count,"won remains locked");
        m = Model(); int wrong = m.SolutionBoard[0,0]%9+1;
        for (int i=0;i<3;i++) { Engine.Erase(m,0,0); Engine.MakeMove(m,0,0,wrong); }
        Check(Engine.TryFinish(m,false),"third error ends game");
        Engine.Undo(m); Check(m.State == GameState.Lost && m.Mistakes == 3,"lost cannot undo into playing");
        Check(SudokuSolver.CountSolutions(new int[9,9]) == 2,"counter stops at second solution");
        var invalid = (int[,])m.SolutionBoard.Clone(); invalid[0,0] = invalid[0,1];
        Check(SudokuSolver.CountSolutions(invalid) == 0,"invalid filled grid rejected");
        Console.WriteLine("PASS: moves, score, hints, notes, undo, terminal states, solver invalidity.");
    }
    private static void GeneratorTests()
    {
        var watch = Stopwatch.StartNew();
        int[] targets = [32,42,50,56];
        for (int d=0;d<4;d++)
        {
            int min=81,max=0;
            for (int sample=0;sample<30;sample++)
            {
                var m = Engine.StartNewGame(d); var copy = (int[,])m.CurrentBoard.Clone();
                Check(SudokuSolver.CountSolutions(m.CurrentBoard) == 1,"generated puzzle unique");
                Check(SudokuSolver.CountSolutions(m.SolutionBoard) == 1 && !m.SolutionBoard.Cast<int>().Contains(0),"valid complete solution");
                Check(copy.Cast<int>().SequenceEqual(m.CurrentBoard.Cast<int>()),"counter does not mutate puzzle");
                int empty = 0;
                for (int r=0;r<9;r++) for (int c=0;c<9;c++)
                {
                    Check(m.IsFixed[r,c] == (m.CurrentBoard[r,c]!=0),"fixed mask");
                    Check(!m.IsFixed[r,c] || m.CurrentBoard[r,c] == m.SolutionBoard[r,c],"fixed clues match solution");
                    if (!m.IsFixed[r,c]) empty++;
                }
                Check(empty > 0 && empty <= targets[d],"bounded target and fallback");
                min=Math.Min(min,empty); max=Math.Max(max,empty);
            }
            Console.WriteLine($"PASS: 30 unique puzzles level {d}, empty cells {min}–{max}, target {targets[d]}.");
        }
        Console.WriteLine($"Generator suite duration: {watch.ElapsedMilliseconds} ms.");
    }
    private static async Task AuthTests()
    {
        var store = new FakeStore(); AuthService.Store = store;
        AuthService.Logout(); AuthService.LoginAsGuest(); var guest = AuthService.CurrentUser!;
        Guid game = Guid.NewGuid();
        Check(await AuthService.RecordGameResultAsync(guest,game,"Dễ",10,30,0,true),"guest save");
        await AuthService.RecordGameResultAsync(guest,game,"Dễ",10,30,0,true);
        Check(guest.TotalGames == 1 && AuthService.GuestHistory.Count == 1,"guest deduplication");
        AuthService.LoginAsGuest("Renamed"); Check(ReferenceEquals(guest,AuthService.CurrentUser) && guest.TotalScore == 10,"guest rename preserves session");
        store.FailRegister = true;
        var result = await AuthService.RegisterAsync("tester","Sample123!","Tester",migrateGuestData:true);
        Check(!result.Success && AuthService.GuestHistory.Count == 1 && guest.TotalScore == 10,"failed migration preserves history and stats");
        store.FailRegister = false;
        result = await AuthService.RegisterAsync("tester","Sample123!","Tester",migrateGuestData:true);
        Check(result.Success && guest.TotalGames == 0 && AuthService.GuestHistory.Count == 0,"clear after commit only");
        var user = result.User!;
        Check(user.TotalGames == 1 && user.TotalWins == 1 && user.TotalScore == 10 && user.HighScore == 10,"migrated stats from history");
        Check(store.Hashes[user.UserId].StartsWith("pbkdf2-sha256$"),"new account hash format");
        Check(!await AuthService.RecordGameResultAsync(guest,Guid.NewGuid(),"Dễ",10,30,0,true),"sealed migrated session rejects late guest result");
        AuthService.Logout(); AuthService.LoginAsGuest(); var next = AuthService.CurrentUser!;
        Check(next.SessionId != guest.SessionId && next.TotalGames == 0 && AuthService.GuestHistory.Count == 0,"new guest session isolated");
        await AuthService.RecordGameResultAsync(next,Guid.NewGuid(),"Dễ",5,40,3,false);
        await AuthService.LoginAsync("tester","Sample123!");
        Check(user.TotalGames == 1 && (await AuthService.GetUserHistoryAsync(next)).Records.Count == 1,"existing login does not transfer guest history");
        var legacy = store.AddAccount("legacy",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("old4"))).ToLowerInvariant());
        Check(!(await AuthService.LoginAsync("legacy","incorrect")).Success && store.Hashes[legacy.UserId].Length == 64,"wrong legacy password does not upgrade");
        Check((await AuthService.LoginAsync("legacy","old4")).Success && store.Hashes[legacy.UserId].StartsWith("pbkdf2-sha256$"),"legacy login upgrades hash");
        Check((await AuthService.LoginAsync("legacy","old4")).Success,"upgraded legacy account logs in");
        string h1=PasswordHasher.Hash("Sample123!"),h2=PasswordHasher.Hash("Sample123!");
        Check(h1!=h2 && PasswordHasher.Verify("Sample123!",h1,out _) && !PasswordHasher.Verify("wrong",h1,out _),"random salt and verification");
        Check(!PasswordHasher.Verify("x","pbkdf2-sha256$999999999$bad$bad",out _),"reject malformed/cost abuse hash");
        Check(!(await AuthService.RegisterAsync(new string('x',51),"Sample123!","Tester")).Success,"username schema length");
        Check(!(await AuthService.RegisterAsync("valid","short","Tester")).Success,"new password requirement");
        Check(!(await AuthService.RegisterAsync("valid","Sample123!",new string('x',101))).Success,"display name schema length");
        store.FailAll = true;
        Check(await AuthService.CheckUsernameExistsAsync("valid") == UsernameStatus.Error,"connection error is not availability");
        Check(!(await AuthService.GetUserHistoryAsync(user)).Success,"connection error is not empty history");
        game=Guid.NewGuid();
        Check(!await AuthService.RecordGameResultAsync(user,game,"Khó",30,20,1,true),"failed saving returns false");
        store.FailAll = false;
        Check(await AuthService.RecordGameResultAsync(user,game,"Khó",30,20,1,true),"retry save succeeds");
        await Task.WhenAll(Enumerable.Range(0,5).Select(_ => AuthService.RecordGameResultAsync(user,game,"Khó",30,20,1,true)));
        Check(store.Histories[user.UserId].Count(h=>h.GameId==game)==1 && user.TotalGames==2 && user.TotalScore==40,"concurrent retries deduplicate and keep aggregate stats");
        store.ThrowAfterCommit = true; game=Guid.NewGuid();
        Check(!await AuthService.RecordGameResultAsync(user,game,"Khó",20,20,1,true),"ambiguous commit not reported as confirmed");
        store.ThrowAfterCommit = false;
        Check(await AuthService.RecordGameResultAsync(user,game,"Khó",20,20,1,true) && store.Histories[user.UserId].Count==3,"ambiguous commit retry is idempotent");
        store.SaveGate = new(TaskCreationOptions.RunContinuationsAsynchronously); game=Guid.NewGuid();
        var pending=AuthService.RecordGameResultAsync(user,game,"Dễ",10,20,0,true);
        AuthService.Logout(); AuthService.LoginAsGuest(); var other=AuthService.CurrentUser!;
        store.SaveGate.SetResult(); await pending; store.SaveGate=null;
        Check(store.Histories[user.UserId].Count==4 && other.TotalGames==0,"async save keeps captured account after identity change");
        var clock=Stopwatch.StartNew(); var check=AuthService.CheckUsernameExistsAsync("available");
        Check(!check.IsCompleted && clock.ElapsedMilliseconds<100,"username query yields without blocking caller"); await check;
        Console.WriteLine("PASS: auth, migration failures/success, guest isolation, PBKDF2/legacy upgrade, errors, retries, identity capture.");
    }
    private static object? Field(object obj,string name) => obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(obj);
    private static void Set(object obj,string name,object value) => obj.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(obj,value);
    private static void Call(object obj,string method,params object?[] args) => obj.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(obj,args);
    private static void PumpUntil(Func<bool> done,int timeout=10000)
    {
        var frame=new DispatcherFrame(); var watch=Stopwatch.StartNew();
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(10) };
        timer.Tick+=(_,_)=> { if(done() || watch.ElapsedMilliseconds>timeout) frame.Continue=false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        Check(done(),"WPF dispatcher timeout");
    }
    private static void WpfTests()
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var store=new FakeStore(); AuthService.Store=store;
        AuthService.Logout(); AuthService.LoginAsGuest();
        var window=new MainWindow();
        PumpUntil(()=>!(bool)Field(window,"_isGenerating")!);
        Call(window,"BtnPencil_Click",window,new RoutedEventArgs());
        Call(window,"BtnPause_Click",window,new RoutedEventArgs());
        Check(((System.Windows.Controls.Primitives.UniformGrid)Field(window,"BoardGrid")!).Visibility==Visibility.Hidden,"pause hides board");
        Call(window,"BtnNewGame_Click",window,new RoutedEventArgs());
        PumpUntil(()=>!(bool)Field(window,"_isGenerating")!);
        CheckReset(window);
        Call(window,"BtnPause_Click",window,new RoutedEventArgs());
        Call(window,"Difficulty_Click",new Button { Tag="3" },new RoutedEventArgs());
        PumpUntil(()=>!(bool)Field(window,"_isGenerating")!); CheckReset(window);
        Call(window,"StartGame",0); Call(window,"StartGame",1);
        PumpUntil(()=>!(bool)Field(window,"_isGenerating")!);
        Check((int)Field(window,"_currentDifficulty")! == 1,"latest generation wins");
        string outcome=""; Set(window,"_showOutcome",new Action<string,string,bool>((message,_,_)=>outcome=message));
        var m=(SudokuModel)Field(window,"_currentModel")!; m.CurrentBoard=(int[,])m.SolutionBoard.Clone();
        Call(window,"HandleGameWon");
        PumpUntil(()=>m.SaveState!=ResultSaveState.Saving);
        Check(m.State==GameState.Won && m.SaveState==ResultSaveState.Saved && outcome.Contains("phiên Khách"),"WPF completion and truthful guest status");
        int games=AuthService.CurrentUser!.TotalGames,score=m.Score;
        Call(window,"HandleGameWon"); Call(window,"ApplyNumberInput",1); Call(window,"BtnUndo_Click",window,new RoutedEventArgs()); Call(window,"BtnErase_Click",window,new RoutedEventArgs());
        Check(AuthService.CurrentUser.TotalGames==games && m.Score==score,"WPF repeated finish and controls cannot alter won game");
        Check(!((DispatcherTimer)Field(window,"_gameTimer")!).IsEnabled,"timer stopped after win");
        window.Close(); Check((bool)Field(window,"_closed")! && !((DispatcherTimer)Field(window,"_gameTimer")!).IsEnabled,"close releases timer and event subscription");

        // Failed save: exercise real FinishGame UI logic without modal messagebox or real SQL.
        AuthService.Logout(); var user=store.AddAccount("uiuser",PasswordHasher.Hash("Sample123!"));
        var loginTask=AuthService.LoginAsync("uiuser","Sample123!");
        PumpUntil(()=>loginTask.IsCompleted); Check(loginTask.Result.Success,"UI test account login");
        var failed=new MainWindow(); PumpUntil(()=>!(bool)Field(failed,"_isGenerating")!);
        outcome=""; Set(failed,"_showOutcome",new Action<string,string,bool>((message,_,_)=>outcome=message));
        store.FailAll=true; m=(SudokuModel)Field(failed,"_currentModel")!; m.CurrentBoard=(int[,])m.SolutionBoard.Clone();
        Call(failed,"HandleGameWon"); PumpUntil(()=>m.SaveState==ResultSaveState.Failed);
        Check(outcome.Contains("Không lưu được") && !outcome.Contains("đã được lưu vào SQL"),"failed save never reports SQL success");
        Check(m.State==GameState.Won && !((DispatcherTimer)Field(failed,"_gameTimer")!).IsEnabled,"database failure does not reopen game");
        failed.Close(); store.FailAll=false;
        var login=new LoginWindow(); var text=(TextBox)Field(login,"txtRegUser")!;
        text.Text="oldname"; PumpUntil(()=>store.UsernameRequests.Contains("oldname")); text.Text="newname";
        PumpUntil(()=>((TextBlock)Field(login,"lblUserCheck")!).Text.Contains("khả dụng"));
        var wait=Stopwatch.StartNew(); PumpUntil(()=>wait.ElapsedMilliseconds>700);
        Check(((TextBlock)Field(login,"lblUserCheck")!).Text.Contains("khả dụng"),"stale username response ignored");
        login.Close();
        var busyLogin=new LoginWindow();
        ((TextBox)Field(busyLogin,"txtLoginUser")!).Text="missing";
        ((PasswordBox)Field(busyLogin,"txtLoginPass")!).Password="wrong";
        store.FindGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
        int before=store.FindRequests;
        Call(busyLogin,"BtnLogin_Click",busyLogin,new RoutedEventArgs());
        Call(busyLogin,"BtnLogin_Click",busyLogin,new RoutedEventArgs());
        Check((bool)Field(busyLogin,"_authBusy")! && store.FindRequests==before+1 && !((Button)Field(busyLogin,"btnConfirmGuest")!).IsEnabled,"repeated login and guest identity switch blocked while busy");
        store.FindGate.SetResult(); PumpUntil(()=>!(bool)Field(busyLogin,"_authBusy")!); store.FindGate=null;
        busyLogin.Close();
        store.FailAll=true;
        var historyWindow=new HistoryWindow();
        PumpUntil(()=>!(bool)Field(historyWindow,"_loading")!);
        Check(((TextBlock)Field(historyWindow,"txtEmptyHistory")!).Text.Contains("Không tải được"),"WPF history failure distinguished from empty");
        store.FailAll=false; Call(historyWindow,"BtnRefresh_Click",historyWindow,new RoutedEventArgs());
        PumpUntil(()=>!(bool)Field(historyWindow,"_loading")!);
        Check(((TextBlock)Field(historyWindow,"txtEmptyHistory")!).Text.Contains("Chưa có"),"WPF empty history after successful refresh");
        historyWindow.Close();
        Console.WriteLine("PASS: actual WPF window/control initialization, pause/new game/difficulty, reset, generation race, completion, failed-save messaging, timer cleanup, stale username response.");
    }
    private static void CheckReset(MainWindow window)
    {
        var m=(SudokuModel)Field(window,"_currentModel")!;
        Check(m.State==GameState.Playing && m.Score==0 && m.Mistakes==0 && m.HintsLeft==3 && m.UndoHistory.Count==0,"new game model reset");
        Check((int)Field(window,"_elapsedSeconds")! == 0 && !(bool)Field(window,"_isPencilMode")!,
            $"timer and pencil reset: seconds={Field(window,"_elapsedSeconds")}, pencil={Field(window,"_isPencilMode")}");
        Check(((System.Windows.Controls.Primitives.UniformGrid)Field(window,"BoardGrid")!).Visibility==Visibility.Visible,"new game board visible");
        Check((int)Field(window,"_selectedRow")! >= 0 && ((Button)Field(window,"btnPencil")!).Foreground!=System.Windows.Media.Brushes.White,"selection and pencil visual reset");
    }
    private static void PvpTests()
    {
        // 1. Elo calculation tests
        var (p1D, p2D, p1New, p2New) = sudokuvip.Pvp.Engine.PvpEloCalculator.Calculate(1200, 1200, true);
        Check(p1D == 16 && p2D == -16 && p1New == 1216 && p2New == 1184, "equal elo win/loss calculation");
        var (draw1, draw2, _, _) = sudokuvip.Pvp.Engine.PvpEloCalculator.Calculate(1200, 1200, false, isDraw: true);
        Check(draw1 == 0 && draw2 == 0, "equal elo draw calculation");

        // 2. Board data serialization
        var model = Engine.StartNewGame(0);
        var board = sudokuvip.Pvp.Models.PvpBoardData.FromModel(model);
        Check(board.TotalEmpty == 32 && board.Clues.Length == 81 && board.Solution.Length == 81, "pvp board data conversion");

        // 3. Message serialization round-trip
        var progressMsg = sudokuvip.Pvp.Network.PvpMessage.Create(sudokuvip.Pvp.Network.PvpMessageType.PlayerProgress, new sudokuvip.Pvp.Network.MsgProgressPayload
        {
            MatchId = "test1", CellIndex = 5, IsCorrect = true, Mistakes = 0, FilledCorrect = 1, TotalEmpty = 32, Score = 10
        });
        string json = progressMsg.ToJson();
        var deserialized = sudokuvip.Pvp.Network.PvpMessage.FromJson(json);
        Check(deserialized != null && deserialized.Type == sudokuvip.Pvp.Network.PvpMessageType.PlayerProgress, "message round trip type");
        var payload = deserialized!.GetPayload<sudokuvip.Pvp.Network.MsgProgressPayload>();
        Check(payload != null && payload.MatchId == "test1" && payload.CellIndex == 5 && payload.IsCorrect, "message round trip payload");

        // 4. AI Bot initialization
        var bot = new sudokuvip.Pvp.Engine.PvpAiBot(board, 0, 1200);
        Check(bot.PlayerInfo != null && bot.PlayerInfo.IsBot && !string.IsNullOrEmpty(bot.PlayerInfo.DisplayName), "pvp ai bot profile generation");

        Console.WriteLine("PASS: PvP Elo calculator, board serialization, network message schema, and AI bot engine.");
    }
}

internal sealed class FakeStore : IAccountStore
{
    public readonly Dictionary<int,UserAccount> Users=new();
    public readonly Dictionary<int,string> Hashes=new();
    public readonly Dictionary<int,List<GameHistoryRecord>> Histories=new();
    public readonly List<string> UsernameRequests=new();
    public bool FailAll,FailRegister,ThrowAfterCommit;
    public TaskCompletionSource? SaveGate;
    public TaskCompletionSource? FindGate;
    public int FindRequests;
    public UserAccount AddAccount(string name,string hash)
    {
        var user=new UserAccount { UserId=Users.Count+1,Username=name,DisplayName=name,CreatedAt=DateTime.Now };
        Users[user.UserId]=user; Hashes[user.UserId]=hash; Histories[user.UserId]=new(); return user;
    }
    public async Task<bool> UsernameExistsAsync(string username,CancellationToken cancellationToken)
    {
        if(FailAll) throw new InvalidOperationException();
        UsernameRequests.Add(username);
        await Task.Delay(username=="oldname"?1000:50); // Deliberately ignore token to verify stale-response handling.
        return username=="oldname" || Users.Values.Any(u=>u.Username.Equals(username,StringComparison.OrdinalIgnoreCase));
    }
    public Task<UserAccount> RegisterAsync(string username,string hash,string displayName,string avatar,IReadOnlyList<GameHistoryRecord> history)
    {
        if(FailAll || FailRegister) throw new InvalidOperationException();
        if(Users.Values.Any(u=>u.Username.Equals(username,StringComparison.OrdinalIgnoreCase))) throw new DuplicateUsernameException();
        var user=AddAccount(username,hash); user.DisplayName=displayName; user.Avatar=avatar;
        Histories[user.UserId]=history.ToList(); AuthService.ApplyStatistics(user,history.ToList()); return Task.FromResult(user);
    }
    public async Task<(UserAccount User,string Hash)?> FindAccountAsync(string username)
    {
        FindRequests++;
        if(FindGate!=null) await FindGate.Task;
        if(FailAll) throw new InvalidOperationException();
        var u=Users.Values.FirstOrDefault(u=>u.Username.Equals(username,StringComparison.OrdinalIgnoreCase));
        return u==null?null:(u,Hashes[u.UserId]);
    }
    public Task UpgradePasswordAsync(int userId,string oldHash,string newHash)
    {
        if(FailAll) throw new InvalidOperationException();
        if(Hashes[userId]==oldHash) Hashes[userId]=newHash; return Task.CompletedTask;
    }
    public async Task SaveResultAsync(GameHistoryRecord record)
    {
        if(SaveGate!=null) await SaveGate.Task;
        if(FailAll) throw new InvalidOperationException();
        if(Histories[record.UserId].All(h=>h.GameId!=record.GameId)) Histories[record.UserId].Add(record);
        if(ThrowAfterCommit) throw new InvalidOperationException();
    }
    public Task<List<GameHistoryRecord>> GetHistoryAsync(int userId)
    {
        if(FailAll) throw new InvalidOperationException();
        return Task.FromResult(Histories[userId].ToList());
    }
}
