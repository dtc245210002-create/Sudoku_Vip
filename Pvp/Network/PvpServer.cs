using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using sudokuvip.Pvp.Engine;
using sudokuvip.Pvp.Models;

namespace sudokuvip.Pvp.Network
{
    public class PvpServer : IDisposable
    {
        internal object SyncRoot { get; } = new();
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ConcurrentDictionary<string, ServerSession> _sessions = new();
        private readonly ConcurrentDictionary<string, ServerRoom> _rooms = new();
        private readonly ConcurrentDictionary<string, ServerMatch> _matches = new();
        private readonly Dictionary<int, List<ServerSession>> _matchQueues = new()
        {
            { 0, new() }, { 1, new() }, { 2, new() }, { 3, new() }
        };
        private readonly object _queueLock = new();
        private readonly SudokuEngine _engine = new();

        public IPAddress ListenAddress { get; private set; } = IPAddress.Loopback;
        public int Port { get; private set; }
        public bool IsRunning => _listener != null;

        public event Action<string>? OnLog;

        public bool Start(int port = 5123, IPAddress? address = null)
        {
            try
            {
                Stop();
                Port = port;
                _cts = new CancellationTokenSource();
                ListenAddress=address ?? IPAddress.Loopback;
                _listener = new TcpListener(ListenAddress, port);
                _listener.Start();
                Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                OnLog?.Invoke($"[PvP Server] Đang lắng nghe trên cổng {port}...");
                var listener=_listener; var token=_cts.Token;
                _ = Task.Run(() => AcceptLoopAsync(listener,token));
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[PvP Server] Lỗi khởi động trên cổng {port}: {ex.Message}");
                Stop();
                return false;
            }
        }

        public void Stop()
        {
            lock (SyncRoot) StopCore();
        }
        private void StopCore()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts=null;
            try { _listener?.Stop(); } catch { }
            _listener = null;
            foreach (var s in _sessions.Values) s.Dispose();
            _sessions.Clear();
            _rooms.Clear();
            foreach (var match in _matches.Values) match.Stop();
            _matches.Clear();
            lock (_queueLock)
            {
                foreach (var list in _matchQueues.Values) list.Clear();
            }
            OnLog?.Invoke("[PvP Server] Đã dừng máy chủ.");
        }

        private async Task AcceptLoopAsync(TcpListener listener,CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var tcpClient = await listener.AcceptTcpClientAsync(token);
                    var session = new ServerSession(tcpClient, this);
                    _sessions[session.SessionId] = session;
                    _ = Task.Run(() => session.RunAsync(token));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    OnLog?.Invoke($"[PvP Server] Lỗi kết nối mới: {ex.Message}");
                }
            }
        }

        internal void HandleDisconnect(ServerSession session)
        {
            lock (SyncRoot) HandleDisconnectCore(session);
        }
        private void HandleDisconnectCore(ServerSession session)
        {
            _sessions.TryRemove(session.SessionId, out _);
            RemoveFromQueue(session);

            // Handle room leaving
            if (session.CurrentRoomId != null && _rooms.TryGetValue(session.CurrentRoomId, out var room))
            {
                room.RemovePlayer(session);
                if (room.IsEmpty)
                {
                    _rooms.TryRemove(room.RoomPin, out _);
                }
                else
                {
                    room.BroadcastUpdate();
                }
            }

            // Handle active match forfeit
            if (session.CurrentMatchId != null && _matches.TryGetValue(session.CurrentMatchId, out var match))
            {
                match.HandleForfeit(session, "Đối thủ mất kết nối.");
            }
        }

        #region QUEUE MATCHMAKING

        internal void Enqueue(ServerSession session, int difficulty)
        {
            LeaveRoom(session);
            RemoveFromQueue(session);
            ServerSession? opponent = null;

            lock (_queueLock)
            {
                if (!_matchQueues.ContainsKey(difficulty)) difficulty = 1;
                var queue = _matchQueues[difficulty];
                if (queue.Count > 0)
                {
                    opponent = queue[0];
                    queue.RemoveAt(0);
                    opponent.QueueDifficulty = null;
                }
                else
                {
                    queue.Add(session);
                    session.QueueDifficulty = difficulty;
                }
            }

            if (opponent != null)
            {
                CreateAndStartMatch(session, opponent, difficulty);
            }
            else
            {
                session.Send(PvpMessageType.QueueStatus, "Đang tìm đối thủ...");
            }
        }

        internal void RemoveFromQueue(ServerSession session)
        {
            lock (_queueLock)
            {
                if (session.QueueDifficulty.HasValue && _matchQueues.TryGetValue(session.QueueDifficulty.Value, out var q))
                {
                    q.Remove(session);
                }
                session.QueueDifficulty = null;
            }
        }

        #endregion

        #region ROOM MANAGEMENT

        internal void CreateRoom(ServerSession session, int difficulty)
        {
            RemoveFromQueue(session); LeaveRoom(session);
            difficulty=Math.Clamp(difficulty,0,3);
            string pin = GenerateRoomPin();
            var room = new ServerRoom(pin, difficulty, session);
            _rooms[pin] = room;
            session.CurrentRoomId = pin;
            room.BroadcastUpdate();
        }

        internal void JoinRoom(ServerSession session, string pin)
        {
            pin = pin.Trim();
            if (session.CurrentRoomId == pin) return;
            if (!_rooms.TryGetValue(pin, out var room))
            {
                session.Send(PvpMessageType.Error, new MsgErrorPayload { Message = "Không tìm thấy phòng với mã PIN này." });
                return;
            }

            if (room.IsFull)
            {
                session.Send(PvpMessageType.Error, new MsgErrorPayload { Message = "Phòng đã đầy người chơi." });
                return;
            }

            if (room.Status != PvpRoomStatus.Waiting)
            {
                session.Send(PvpMessageType.Error, new MsgErrorPayload { Message = "Phòng đang trong trận đấu." });
                return;
            }

            RemoveFromQueue(session); LeaveRoom(session);
            room.AddPlayer(session);
            session.CurrentRoomId = pin;
            room.BroadcastUpdate();
        }

        internal void LeaveRoom(ServerSession session)
        {
            if (session.CurrentRoomId != null && _rooms.TryGetValue(session.CurrentRoomId, out var room))
            {
                room.RemovePlayer(session);
                session.CurrentRoomId = null;
                if (room.IsEmpty)
                {
                    _rooms.TryRemove(room.RoomPin, out _);
                }
                else
                {
                    room.BroadcastUpdate();
                }
            }
        }

        internal void ToggleReady(ServerSession session, bool isReady)
        {
            if (session.CurrentRoomId != null && _rooms.TryGetValue(session.CurrentRoomId, out var room))
            {
                if (room.Status != PvpRoomStatus.Waiting || session.CurrentMatchId != null) return;
                room.SetReady(session, isReady);
                room.BroadcastUpdate();

                if (room.CanStart)
                {
                    room.Status = PvpRoomStatus.InGame;
                    CreateAndStartMatch(room.Player1Session!, room.Player2Session!, room.Difficulty);
                }
            }
        }

        private string GenerateRoomPin()
        {
            var rand = Random.Shared;
            for (int i = 0; i < 50; i++)
            {
                string pin = rand.Next(1000, 9999).ToString();
                if (!_rooms.ContainsKey(pin)) return pin;
            }
            return rand.Next(10000, 99999).ToString();
        }

        #endregion

        #region MATCH CREATION & RUNTIME

        internal void StartBotMatch(ServerSession session, int difficulty)
        {
            RemoveFromQueue(session); LeaveRoom(session);
            difficulty = Math.Clamp(difficulty,0,3);
            var model = _engine.StartNewGame(difficulty);
            var boardData = PvpBoardData.FromModel(model);
            var bot = new PvpAiBot(boardData, difficulty, session.Player.EloRating);

            var match = new ServerMatch(this, session, null, bot, boardData, difficulty);
            _matches[match.MatchId] = match;
            session.CurrentMatchId = match.MatchId;
            match.Start();
        }

        internal void CreateAndStartMatch(ServerSession session1, ServerSession session2, int difficulty)
        {
            var model = _engine.StartNewGame(difficulty);
            var boardData = PvpBoardData.FromModel(model);

            var match = new ServerMatch(this, session1, session2, null, boardData, difficulty);
            _matches[match.MatchId] = match;
            session1.CurrentMatchId = match.MatchId;
            session2.CurrentMatchId = match.MatchId;
            match.Start();
        }

        internal void HandleProgress(ServerSession session, MsgMovePayload prog)
        {
            if (session.CurrentMatchId == prog.MatchId && _matches.TryGetValue(prog.MatchId, out var match))
            {
                match.HandleMove(session, prog);
            }
        }

        internal void HandleEmote(ServerSession session, string emote)
        {
            if (session.CurrentMatchId != null && _matches.TryGetValue(session.CurrentMatchId, out var match))
            {
                match.HandleEmote(session.Player.Id, emote);
            }
        }

        internal void HandleSurrender(ServerSession session)
        {
            if (session.CurrentMatchId != null && _matches.TryGetValue(session.CurrentMatchId, out var match))
            {
                match.HandleForfeit(session, $"{session.Player.DisplayName} đã đầu hàng.");
            }
        }

        internal void HandleMatchFinished(ServerMatch match)
        {
            _matches.TryRemove(match.MatchId, out _);
            foreach (var session in _sessions.Values.Where(s => s.CurrentMatchId == match.MatchId).ToArray())
            { session.CurrentMatchId = null; session.Player.IsReady = false; LeaveRoom(session); }
        }

        #endregion

        public void Dispose()
        {
            Stop();
        }
    }

    internal class ServerSession : IDisposable
    {
        public string SessionId { get; } = Guid.NewGuid().ToString("N");
        public PvpPlayer Player { get; set; } = new();
        public int? QueueDifficulty { get; set; }
        public string? CurrentRoomId { get; set; }
        public string? CurrentMatchId { get; set; }

        private readonly TcpClient _client;
        private readonly PvpServer _server;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private volatile bool _disposed;
        private bool _helloReceived;

        public ServerSession(TcpClient client, PvpServer server)
        {
            _client = client;
            _server = server;
            var stream = client.GetStream();
            _reader = new StreamReader(stream, Encoding.UTF8);
            _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        }

        public async Task RunAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && !_disposed)
                {
                    string? line = await _reader.ReadLineAsync(token);
                    if (line == null) break;

                    var msg = PvpMessage.FromJson(line);
                    if (msg != null)
                    {
                        lock (_server.SyncRoot) ProcessMessage(msg);
                    }
                }
            }
            catch { }
            finally
            {
                _server.HandleDisconnect(this);
                Dispose();
            }
        }

        private void ProcessMessage(PvpMessage msg)
        {
            if (_disposed) return;
            if (msg.Type != PvpMessageType.Hello && !_helloReceived) return;
            if (CurrentMatchId != null && msg.Type is PvpMessageType.JoinQueue or PvpMessageType.CreateRoom or PvpMessageType.JoinRoom or PvpMessageType.StartBotMatch)
            { Send(PvpMessageType.Error,new MsgErrorPayload { Message="Bạn đang trong một trận đấu." }); return; }
            switch (msg.Type)
            {
                case PvpMessageType.Hello:
                    var hello = msg.GetPayload<PvpPlayer>();
                    if (hello != null && !_helloReceived)
                    {
                        Player = hello;
                        Player.Id = SessionId;
                        Player.IsBot = false; Player.IsReady = false;
                        _helloReceived = true;
                        Send(PvpMessageType.HelloAck, Player);
                    }
                    break;

                case PvpMessageType.JoinQueue:
                    var jq = msg.GetPayload<MsgJoinQueuePayload>();
                    _server.Enqueue(this, jq?.Difficulty ?? 1);
                    break;

                case PvpMessageType.LeaveQueue:
                    _server.RemoveFromQueue(this);
                    break;

                case PvpMessageType.CreateRoom:
                    var cr = msg.GetPayload<MsgCreateRoomPayload>();
                    _server.CreateRoom(this, cr?.Difficulty ?? 1);
                    break;

                case PvpMessageType.JoinRoom:
                    var jr = msg.GetPayload<MsgJoinRoomPayload>();
                    if (jr != null) _server.JoinRoom(this, jr.RoomPin);
                    break;

                case PvpMessageType.LeaveRoom:
                    _server.LeaveRoom(this);
                    break;

                case PvpMessageType.ToggleReady:
                    var isReady = msg.GetPayload<bool>();
                    _server.ToggleReady(this, isReady);
                    break;

                case PvpMessageType.StartBotMatch:
                    int diff = msg.GetPayload<int>();
                    _server.StartBotMatch(this, diff);
                    break;

                case PvpMessageType.PlayerMove:
                    var prog = msg.GetPayload<MsgMovePayload>();
                    if (prog != null && CurrentMatchId != null)
                    {
                        _server.HandleProgress(this, prog);
                    }
                    break;

                case PvpMessageType.SendEmote:
                    var emo = msg.GetPayload<MsgEmotePayload>();
                    if (emo != null && CurrentMatchId != null)
                    {
                        _server.HandleEmote(this, emo.Emote);
                    }
                    break;

                case PvpMessageType.Surrender:
                    if (CurrentMatchId != null && msg.GetPayload<string>() == CurrentMatchId)
                    {
                        _server.HandleSurrender(this);
                    }
                    break;
            }
        }

        public void Send<T>(PvpMessageType type, T payload)
        {
            Send(PvpMessage.Create(type, payload));
        }

        public void Send(PvpMessageType type)
        {
            Send(PvpMessage.Create(type));
        }

        public void Send(PvpMessage message)
        {
            if (_disposed) return;
            string json = message.ToJson();
            _ = SendOrderedAsync(json);
        }

        private async Task SendOrderedAsync(string json)
        {
                await _sendLock.WaitAsync();
                try
                {
                    if (!_disposed)
                    {
                        await _writer.WriteLineAsync(json);
                        await _writer.FlushAsync();
                    }
                }
                catch { }
                finally
                {
                    _sendLock.Release();
                }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _client.Close(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _writer.Dispose(); } catch { }
            try { _client.Dispose(); } catch { }
            // Pending writes release this semaphore after the socket closes.
        }
    }

    internal class ServerRoom
    {
        public string RoomPin { get; }
        public int Difficulty { get; }
        public ServerSession? Player1Session { get; private set; }
        public ServerSession? Player2Session { get; private set; }
        public PvpRoomStatus Status { get; set; } = PvpRoomStatus.Waiting;

        public bool IsEmpty => Player1Session == null && Player2Session == null;
        public bool IsFull => Player1Session != null && Player2Session != null;
        public bool CanStart => Status == PvpRoomStatus.Waiting && IsFull && Player1Session!.Player.IsReady && Player2Session!.Player.IsReady;

        public ServerRoom(string pin, int difficulty, ServerSession host)
        {
            RoomPin = pin;
            Difficulty = difficulty;
            Player1Session = host;
            host.Player.IsReady = false;
        }

        public void AddPlayer(ServerSession session)
        {
            if (Player1Session == null) Player1Session = session;
            else if (Player2Session == null) Player2Session = session;
            session.Player.IsReady = false;
        }

        public void RemovePlayer(ServerSession session)
        {
            if (Player1Session == session)
            {
                Player1Session = Player2Session;
                Player2Session = null;
            }
            else if (Player2Session == session)
            {
                Player2Session = null;
            }
        }

        public void SetReady(ServerSession session, bool isReady)
        {
            if (Player1Session == session) Player1Session.Player.IsReady = isReady;
            else if (Player2Session == session) Player2Session.Player.IsReady = isReady;
        }

        public void BroadcastUpdate()
        {
            var info = new PvpRoomInfo
            {
                RoomPin = RoomPin,
                Difficulty = Difficulty,
                HostId = Player1Session?.Player.Id ?? string.Empty,
                Player1 = Player1Session?.Player,
                Player2 = Player2Session?.Player,
                Status = Status
            };
            Player1Session?.Send(PvpMessageType.RoomUpdate, info);
            Player2Session?.Send(PvpMessageType.RoomUpdate, info);
        }
    }

    internal class ServerMatch
    {
        public string MatchId { get; } = Guid.NewGuid().ToString("N");
        private readonly PvpServer _server;
        private readonly ServerSession _p1;
        private readonly ServerSession? _p2;
        private readonly PvpAiBot? _bot;
        private readonly PvpBoardData _board;
        private readonly int _difficulty;

        private readonly PvpMatchStats _stats1;
        private readonly PvpMatchStats _stats2;
        private readonly DateTime _startTime = DateTime.Now;
        private bool _isFinished;
        private readonly object _lock;
        private readonly PvpBoardState _state1;
        private readonly PvpBoardState _state2;

        public ServerMatch(PvpServer server, ServerSession p1, ServerSession? p2, PvpAiBot? bot, PvpBoardData board, int difficulty)
        {
            _server = server;
            _lock = server.SyncRoot;
            _state1 = new(board); _state2 = new(board);
            _p1 = p1;
            _p2 = p2;
            _bot = bot;
            _board = board;
            _difficulty = difficulty;

            _stats1 = new PvpMatchStats { PlayerId = p1.Player.Id, DisplayName = p1.Player.DisplayName, TotalEmpty = board.TotalEmpty };
            _stats2 = new PvpMatchStats
            {
                PlayerId = p2?.Player.Id ?? bot?.PlayerInfo.Id ?? "opponent",
                DisplayName = p2?.Player.DisplayName ?? bot?.PlayerInfo.DisplayName ?? "Đối thủ",
                TotalEmpty = board.TotalEmpty
            };
        }

        public void Start()
        {
            // Send StartMatch to P1
            var p2Info = _p2?.Player ?? _bot!.PlayerInfo;
            _p1.Send(PvpMessageType.StartMatch, new MsgStartMatchPayload
            {
                MatchId = MatchId,
                Opponent = p2Info,
                Board = _board,
                Difficulty = _difficulty
            });

            // Send StartMatch to P2 if human
            if (_p2 != null)
            {
                _p2.Send(PvpMessageType.StartMatch, new MsgStartMatchPayload
                {
                    MatchId = MatchId,
                    Opponent = _p1.Player,
                    Board = _board,
                    Difficulty = _difficulty
                });
            }
            else if (_bot != null)
            {
                // Wire up bot events
                _bot.OnProgressUpdated += (idx, correct, mistakes, count, score) =>
                {
                    lock (_lock)
                    {
                        if (_isFinished) return;
                        int value = correct ? _board.Solution[idx] : _board.Solution[idx] % 9 + 1;
                        var operation = new MsgMovePayload { MatchId=MatchId,Sequence=_state2.Sequence+1,CellIndex=idx,Value=value };
                        if (_state2.Apply(operation,out int cell)) PublishProgress(false,cell);
                    }
                };
                _bot.OnEmoteSent += (emote) =>
                {
                    _p1.Send(PvpMessageType.OpponentEmote, new MsgEmotePayload { Emote = emote });
                };
                _bot.StartPlaying();
            }
        }

        public void Stop() { lock (_lock) { _isFinished=true; _bot?.Stop(); } }
        public void HandleMove(ServerSession sender, MsgMovePayload move)
        {
            lock (_lock)
            {
                if (_isFinished || move.MatchId != MatchId || (sender != _p1 && sender != _p2)) return;
                bool isP1 = sender == _p1;
                var state = isP1 ? _state1 : _state2;
                if (!state.Apply(move,out int changedCell))
                { sender.Send(PvpMessageType.Error,new MsgErrorPayload {Message="Bước đi không hợp lệ hoặc sai thứ tự."}); return; }
                PublishProgress(isP1,changedCell);
            }
        }
        private void PublishProgress(bool isP1, int cell)
        {
            var state = isP1 ? _state1 : _state2;
            var stats = isP1 ? _stats1 : _stats2;
            var progress = state.Progress(MatchId,cell);
            stats.FilledCorrect=progress.FilledCorrect; stats.Mistakes=progress.Mistakes; stats.Score=progress.Score;
            (isP1 ? _p1 : _p2)?.Send(PvpMessageType.LocalProgress,progress);
            (isP1 ? _p2 : _p1)?.Send(PvpMessageType.OpponentProgress,progress);
            if (state.Filled == state.TotalEmpty)
                EndMatch(stats.PlayerId,"Hoàn thành toàn bộ bàn cờ!");
            else if (state.Model.Mistakes >= 3)
                EndMatch(isP1 ? _stats2.PlayerId : _stats1.PlayerId,$"{stats.DisplayName} phạm 3 lỗi (K.O)!");
        }

        public void HandleEmote(string senderId, string emote)
        {
            if (senderId == _p1.Player.Id)
            {
                _p2?.Send(PvpMessageType.OpponentEmote, new MsgEmotePayload { Emote = emote });
                _bot?.HandlePlayerEmote(emote);
            }
            else
            {
                _p1.Send(PvpMessageType.OpponentEmote, new MsgEmotePayload { Emote = emote });
            }
        }

        public void HandleForfeit(ServerSession forfeiter, string reason)
        {
            lock (_lock)
            {
                if (_isFinished) return;
                bool isP1 = forfeiter == _p1;
                string winnerId = isP1 ? _stats2.PlayerId : _p1.Player.Id;
                if (isP1) _stats1.Surrendered = true; else _stats2.Surrendered = true;
                EndMatch(winnerId, reason);
            }
        }

        private void EndMatch(string winnerId, string reason)
        {
            if (_isFinished) return;
            _isFinished = true;
            _bot?.Stop();

            int duration = (int)(DateTime.Now - _startTime).TotalSeconds;
            _stats1.DurationSeconds = duration;
            _stats2.DurationSeconds = duration;

            bool p1Won = winnerId == _p1.Player.Id;
            var oppPlayer = _p2?.Player ?? _bot!.PlayerInfo;

            var (p1Delta, p2Delta, p1New, p2New) = PvpEloCalculator.Calculate(
                _p1.Player.EloRating, oppPlayer.EloRating, p1Won);

            _p1.Player.EloRating = p1New;
            oppPlayer.EloRating = p2New;

            var result = new PvpMatchResult
            {
                MatchId = MatchId,
                Difficulty = _difficulty,
                WinnerId = winnerId,
                Reason = reason,
                Player1 = _p1.Player,
                Player2 = oppPlayer,
                P1Stats = _stats1,
                P2Stats = _stats2,
                P1EloBefore = _p1.Player.EloRating - p1Delta,
                P1EloAfter = p1New,
                P2EloBefore = oppPlayer.EloRating - p2Delta,
                P2EloAfter = p2New,
                EloDelta = Math.Abs(p1Delta)
            };

            _p1.Send(PvpMessageType.MatchOver, result);
            _p2?.Send(PvpMessageType.MatchOver, result);

            _server.HandleMatchFinished(this);
        }
    }
}
