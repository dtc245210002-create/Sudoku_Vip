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

        public int Port { get; private set; }
        public bool IsRunning => _listener != null;

        public event Action<string>? OnLog;

        public bool Start(int port = 5123)
        {
            try
            {
                Stop();
                Port = port;
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                OnLog?.Invoke($"[PvP Server] Đang lắng nghe trên cổng {port}...");
                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[PvP Server] Lỗi khởi động trên cổng {port}: {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener?.Stop(); } catch { }
            _listener = null;
            foreach (var s in _sessions.Values) s.Dispose();
            _sessions.Clear();
            _rooms.Clear();
            _matches.Clear();
            lock (_queueLock)
            {
                foreach (var list in _matchQueues.Values) list.Clear();
            }
            OnLog?.Invoke("[PvP Server] Đã dừng máy chủ.");
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _listener != null)
            {
                try
                {
                    var tcpClient = await _listener.AcceptTcpClientAsync(token);
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
            string pin = GenerateRoomPin();
            var room = new ServerRoom(pin, difficulty, session);
            _rooms[pin] = room;
            session.CurrentRoomId = pin;
            room.BroadcastUpdate();
        }

        internal void JoinRoom(ServerSession session, string pin)
        {
            pin = pin.Trim();
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

        internal void HandleProgress(ServerSession session, MsgProgressPayload prog)
        {
            if (session.CurrentMatchId != null && _matches.TryGetValue(session.CurrentMatchId, out var match))
            {
                match.HandleProgress(session.Player.Id, prog.CellIndex, prog.IsCorrect, prog.Mistakes, prog.FilledCorrect, prog.Score);
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
        private bool _disposed;

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
                        ProcessMessage(msg);
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
            switch (msg.Type)
            {
                case PvpMessageType.Hello:
                    var hello = msg.GetPayload<PvpPlayer>();
                    if (hello != null)
                    {
                        Player = hello;
                        Player.Id = SessionId;
                        Send(PvpMessageType.HelloAck, "OK");
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

                case PvpMessageType.PlayerProgress:
                    var prog = msg.GetPayload<MsgProgressPayload>();
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
                    if (CurrentMatchId != null)
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
            _ = Task.Run(async () =>
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
            });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _client.Close(); } catch { }
            try { _reader.Dispose(); } catch { }
            try { _writer.Dispose(); } catch { }
            try { _client.Dispose(); } catch { }
            _sendLock.Dispose();
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
        public bool CanStart => IsFull && Player1Session!.Player.IsReady && Player2Session!.Player.IsReady;

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
        private readonly object _lock = new();

        public ServerMatch(PvpServer server, ServerSession p1, ServerSession? p2, PvpAiBot? bot, PvpBoardData board, int difficulty)
        {
            _server = server;
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
                    HandleProgress(_stats2.PlayerId, idx, correct, mistakes, count, score);
                };
                _bot.OnEmoteSent += (emote) =>
                {
                    _p1.Send(PvpMessageType.OpponentEmote, new MsgEmotePayload { Emote = emote });
                };
                _bot.StartPlaying();
            }
        }

        public void HandleProgress(string playerId, int cellIndex, bool isCorrect, int mistakes, int filledCorrect, int score)
        {
            lock (_lock)
            {
                if (_isFinished) return;

                bool isP1 = playerId == _p1.Player.Id;
                var currentStats = isP1 ? _stats1 : _stats2;
                currentStats.FilledCorrect = filledCorrect;
                currentStats.Mistakes = mistakes;
                currentStats.Score = score;

                // Forward to opponent
                var progPayload = new MsgProgressPayload
                {
                    MatchId = MatchId,
                    CellIndex = cellIndex,
                    IsCorrect = isCorrect,
                    Mistakes = mistakes,
                    FilledCorrect = filledCorrect,
                    TotalEmpty = _board.TotalEmpty,
                    Score = score
                };

                if (isP1)
                {
                    _p2?.Send(PvpMessageType.OpponentProgress, progPayload);
                }
                else
                {
                    _p1.Send(PvpMessageType.OpponentProgress, progPayload);
                }

                // Check Win Conditions
                // 1. Player solved the entire board!
                if (filledCorrect >= _board.TotalEmpty)
                {
                    EndMatch(isP1 ? _p1.Player.Id : _stats2.PlayerId, "Hoàn thành toàn bộ bàn cờ!");
                    return;
                }

                // 2. Player exceeded 3 mistakes -> Opponent wins by KO!
                if (mistakes >= 3)
                {
                    EndMatch(isP1 ? _stats2.PlayerId : _p1.Player.Id, $"{currentStats.DisplayName} phạm 3 lỗi (K.O)!");
                    return;
                }
            }
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
