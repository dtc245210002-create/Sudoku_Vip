using sudokuvip.Models;
using sudokuvip.Pvp.Models;
using sudokuvip.Pvp.Network;
using sudokuvip.Services;

namespace sudokuvip.Pvp;

public class PvpManager : IDisposable
{
    private static readonly Lazy<PvpManager> Singleton = new(() => new());
    public static PvpManager Instance => Singleton.Value;
    public PvpServer Server { get; } = new();
    public PvpClient Client { get; } = new();
    public bool IsServerHost => Server.IsRunning;
    public PvpPlayer? LocalPlayer { get; private set; }
    public string? ActiveMatchId { get; private set; }
    public bool IsReady => Client.IsConnected && LocalPlayer != null && ReferenceEquals(_owner,AuthService.CurrentUser);
    public string ServerHost => _host;
    public int ServerPort => _port;
    private UserAccount? _owner;
    private string _host = "";
    private int _port;
    private TaskCompletionSource<PvpPlayer>? _hello;
    private readonly SemaphoreSlim _connectLock = new(1,1);
    private readonly object _resultLock = new();
    private readonly Dictionary<string,(UserAccount? Owner,string PlayerId)> _owners = new();
    private readonly HashSet<string> _presented = new();
    private readonly Dictionary<string,(PvpMatchResult Result,UserAccount Owner,string PlayerId)> _pending = new();
    public event Action<string>? OnQueueStatus;
    public event Action<PvpRoomInfo>? OnRoomUpdated;
    public event Action<MsgStartMatchPayload>? OnMatchStarted;
    public event Action<MsgProgressPayload>? OnOpponentProgress;
    public event Action<MsgProgressPayload>? OnLocalProgress;
    public event Action<string>? OnOpponentEmote;
    public event Action<PvpMatchResult>? OnMatchOver;
    public event Action<string>? OnError;
    public event Action? OnDisconnected;

    public PvpManager()
    {
        Client.OnMessageReceived += HandleServerMessage;
        Client.OnError += err => OnError?.Invoke(err);
        Client.OnDisconnected += () =>
        {
            LocalPlayer=null; ActiveMatchId=null;
            _hello?.TrySetException(new System.IO.IOException("Kết nối đã đóng."));
            OnDisconnected?.Invoke();
        };
    }
    public async Task<bool> EnsureConnectedAsync(string host="127.0.0.1",int port=5123)
    {
        await _connectLock.WaitAsync();
        try
        {
            var user = AuthService.CurrentUser;
            if (IsReady && _host==host && _port==port && LocalPlayer!.DisplayName==user?.DisplayName && LocalPlayer.Avatar==user?.Avatar) return true;
            Client.Disconnect();
            await RetryPendingResultsAsync();
            _owner=user; _host=host; _port=port;
            _hello = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!await Client.ConnectAsync(host,port)) return false;
            await Client.SendHello(new PvpPlayer { Username=user?.Username ?? "guest",DisplayName=user?.DisplayName ?? "Khách",
                Avatar=user?.Avatar ?? "👤",EloRating=user?.EloRating ?? 1200 });
            var player = await _hello.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!Client.IsConnected || !ReferenceEquals(user,AuthService.CurrentUser)) { Client.Disconnect(); return false; }
            LocalPlayer=player;
            return true;
        }
        catch (Exception ex) { Client.Disconnect(); OnError?.Invoke($"Không hoàn tất kết nối PvP: {ex.Message}"); return false; }
        finally { _connectLock.Release(); }
    }
    private void HandleServerMessage(PvpMessage msg)
    {
        switch (msg.Type)
        {
            case PvpMessageType.HelloAck:
                var player=msg.GetPayload<PvpPlayer>();
                if (player != null && !string.IsNullOrWhiteSpace(player.Id)) _hello?.TrySetResult(player);
                break;
            case PvpMessageType.QueueStatus: OnQueueStatus?.Invoke(msg.GetPayload<string>() ?? ""); break;
            case PvpMessageType.RoomUpdate:
                var room=msg.GetPayload<PvpRoomInfo>(); if (room!=null && ActiveMatchId==null) OnRoomUpdated?.Invoke(room); break;
            case PvpMessageType.StartMatch:
                var match=msg.GetPayload<MsgStartMatchPayload>();
                if (match!=null && LocalPlayer!=null && ActiveMatchId==null)
                {
                    ActiveMatchId=match.MatchId;
                    lock (_resultLock) _owners[match.MatchId]=(_owner,LocalPlayer.Id);
                    OnMatchStarted?.Invoke(match);
                }
                break;
            case PvpMessageType.OpponentProgress:
            case PvpMessageType.LocalProgress:
                var progress=msg.GetPayload<MsgProgressPayload>();
                if (progress!=null && progress.MatchId==ActiveMatchId)
                { if (msg.Type==PvpMessageType.LocalProgress) OnLocalProgress?.Invoke(progress); else OnOpponentProgress?.Invoke(progress); }
                break;
            case PvpMessageType.OpponentEmote:
                var emote=msg.GetPayload<MsgEmotePayload>(); if (emote!=null && ActiveMatchId!=null) OnOpponentEmote?.Invoke(emote.Emote); break;
            case PvpMessageType.MatchOver:
                var result=msg.GetPayload<PvpMatchResult>();
                if (result!=null) HandleResult(result);
                break;
            case PvpMessageType.Error:
                var error=msg.GetPayload<MsgErrorPayload>(); if (error!=null) OnError?.Invoke(error.Message); break;
        }
    }
    private void HandleResult(PvpMatchResult result)
    {
        (UserAccount? Owner,string PlayerId) identity;
        lock (_resultLock)
        {
            if (!_owners.TryGetValue(result.MatchId,out identity) || !_presented.Add(result.MatchId)) return;
            if (result.Player1.Id!=identity.PlayerId && result.Player2.Id!=identity.PlayerId) { _presented.Remove(result.MatchId); return; }
            if (identity.Owner!=null) _pending[result.MatchId]=(result,identity.Owner,identity.PlayerId);
        }
        if (ActiveMatchId==result.MatchId) ActiveMatchId=null;
        if (LocalPlayer?.Id==identity.PlayerId) LocalPlayer.EloRating = result.Player1.Id==identity.PlayerId ? result.P1EloAfter : result.P2EloAfter;
        // Present promptly; persistence keeps its captured owner and can be retried.
        _ = SaveResultAsync(result.MatchId);
        OnMatchOver?.Invoke(result);
    }
    private async Task SaveResultAsync(string matchId)
    {
        (PvpMatchResult Result,UserAccount Owner,string PlayerId) pending;
        lock (_resultLock) { if (!_pending.TryGetValue(matchId,out pending)) return; }
        bool saved=await AuthService.RecordPvpResultAsync(pending.Owner,pending.Result,pending.PlayerId);
        if (saved) { lock (_resultLock) _pending.Remove(matchId); }
        else OnError?.Invoke("Kết quả PvP chưa lưu được. Dùng ‘Thử lưu lại’ trong sảnh sau khi kiểm tra kết nối SQL.");
    }
    public async Task RetryPendingResultsAsync()
    {
        string[] matches; lock (_resultLock) matches=_pending.Keys.ToArray();
        foreach (var id in matches) await SaveResultAsync(id);
    }
    private Task ReadyCommand(Func<Task> command)
    {
        if (!IsReady) { OnError?.Invoke("Hãy kết nối máy chủ trước khi chơi."); return Task.CompletedTask; }
        return command();
    }
    public Task JoinQueue(int difficulty) => ReadyCommand(()=>Client.JoinQueue(difficulty));
    public Task LeaveQueue() => Client.LeaveQueue();
    public Task CreateRoom(int difficulty) => ReadyCommand(()=>Client.CreateRoom(difficulty));
    public Task JoinRoom(string pin) => ReadyCommand(()=>Client.JoinRoom(pin));
    public Task LeaveRoom() => Client.LeaveRoom();
    public Task ToggleReady(bool ready) => ReadyCommand(()=>Client.ToggleReady(ready));
    public Task StartBotMatch(int difficulty) => ReadyCommand(()=>Client.StartBotMatch(difficulty));
    public Task SendMove(MsgMovePayload move) => ReadyCommand(()=>Client.SendAsync(PvpMessageType.PlayerMove,move));
    public Task SendEmote(string emote) => ReadyCommand(()=>Client.SendEmote(emote));
    public Task Surrender(string matchId) => Client.Surrender(matchId);
    public void LeaveLobby() => Client.Disconnect();
    public void Dispose() { Client.Dispose(); Server.Dispose(); }
}
