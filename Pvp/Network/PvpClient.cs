using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using sudokuvip.Pvp.Models;

namespace sudokuvip.Pvp.Network
{
    public class PvpClient : IDisposable
    {
        private sealed class Connection : IDisposable
        {
            public readonly TcpClient Tcp = new();
            public readonly CancellationTokenSource Cts = new();
            public StreamReader Reader = null!;
            public StreamWriter Writer = null!;
            public void Dispose() { Cts.Cancel(); Tcp.Dispose(); }
        }
        private Connection? _connection;
        private readonly SemaphoreSlim _sendLock = new(1,1);
        private readonly SemaphoreSlim _connectLock = new(1,1);
        public bool IsConnected => _connection?.Tcp.Connected == true;
        public PvpPlayer? CurrentPlayer { get; private set; }
        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action<PvpMessage>? OnMessageReceived;
        public event Action<string>? OnError;

        public async Task<bool> ConnectAsync(string host = "127.0.0.1", int port = 5123)
        {
            await _connectLock.WaitAsync();
            Connection connection = new();
            try
            {
                Disconnect();
                _connection = connection;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(connection.Cts.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await connection.Tcp.ConnectAsync(host,port,timeout.Token);
                var stream = connection.Tcp.GetStream();
                connection.Reader = new StreamReader(stream,Encoding.UTF8);
                connection.Writer = new StreamWriter(stream,Encoding.UTF8) { AutoFlush=true };
                if (!ReferenceEquals(_connection,connection)) return false;
                OnConnected?.Invoke();
                _ = ReceiveLoopAsync(connection);
                return true;
            }
            catch (Exception ex)
            {
                Disconnect(connection);
                OnError?.Invoke($"Không thể kết nối máy chủ: {ex.Message}");
                return false;
            }
            finally { _connectLock.Release(); }
        }
        public void Disconnect()
        {
            var connection = Interlocked.Exchange(ref _connection,null);
            if (connection != null) { connection.Dispose(); OnDisconnected?.Invoke(); }
            CurrentPlayer = null;
        }
        private void Disconnect(Connection connection)
        {
            if (Interlocked.CompareExchange(ref _connection,null,connection) == connection)
            { connection.Dispose(); CurrentPlayer=null; OnDisconnected?.Invoke(); }
        }
        public async Task SendAsync(PvpMessage message)
        {
            var connection = _connection;
            if (connection?.Writer == null) return;
            await _sendLock.WaitAsync();
            try
            {
                if (ReferenceEquals(_connection,connection))
                    await connection.Writer.WriteLineAsync(message.ToJson());
            }
            catch (Exception ex) { OnError?.Invoke($"Lỗi gửi gói tin: {ex.Message}"); Disconnect(connection); }
            finally { _sendLock.Release(); }
        }

        public Task SendAsync<T>(PvpMessageType type, T payload)
        {
            return SendAsync(PvpMessage.Create(type, payload));
        }

        public Task SendAsync(PvpMessageType type)
        {
            return SendAsync(PvpMessage.Create(type));
        }

        public Task SendHello(PvpPlayer player)
        {
            CurrentPlayer = player;
            return SendAsync(PvpMessageType.Hello, player);
        }

        public Task JoinQueue(int difficulty)
        {
            return SendAsync(PvpMessageType.JoinQueue, new MsgJoinQueuePayload { Difficulty = difficulty });
        }

        public Task LeaveQueue()
        {
            return SendAsync(PvpMessageType.LeaveQueue);
        }

        public Task CreateRoom(int difficulty)
        {
            return SendAsync(PvpMessageType.CreateRoom, new MsgCreateRoomPayload { Difficulty = difficulty });
        }

        public Task JoinRoom(string pin)
        {
            return SendAsync(PvpMessageType.JoinRoom, new MsgJoinRoomPayload { RoomPin = pin });
        }

        public Task LeaveRoom()
        {
            return SendAsync(PvpMessageType.LeaveRoom);
        }

        public Task ToggleReady(bool isReady)
        {
            return SendAsync(PvpMessageType.ToggleReady, isReady);
        }

        public Task StartBotMatch(int difficulty)
        {
            return SendAsync(PvpMessageType.StartBotMatch, difficulty);
        }

        public Task SendProgress(string matchId, int cellIndex, bool isCorrect, int mistakes, int filledCorrect, int totalEmpty, int score)
        {
            return SendAsync(PvpMessageType.PlayerProgress, new MsgProgressPayload
            {
                MatchId = matchId,
                CellIndex = cellIndex,
                IsCorrect = isCorrect,
                Mistakes = mistakes,
                FilledCorrect = filledCorrect,
                TotalEmpty = totalEmpty,
                Score = score
            });
        }

        public Task SendEmote(string emote)
        {
            return SendAsync(PvpMessageType.SendEmote, new MsgEmotePayload { Emote = emote });
        }

        public Task Surrender(string matchId)
        {
            return SendAsync(PvpMessageType.Surrender, matchId);
        }

        public Task RequestRematch(string matchId)
        {
            return SendAsync(PvpMessageType.RequestRematch, matchId);
        }

        private async Task ReceiveLoopAsync(Connection connection)
        {
            try
            {
                while (!connection.Cts.IsCancellationRequested)
                {
                    string? line = await connection.Reader.ReadLineAsync(connection.Cts.Token);
                    if (line == null) break;
                    var msg = PvpMessage.FromJson(line);
                    if (msg != null && ReferenceEquals(_connection,connection))
                    {
                        if (msg.Type == PvpMessageType.HelloAck) CurrentPlayer = msg.GetPayload<PvpPlayer>();
                        OnMessageReceived?.Invoke(msg);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!connection.Cts.IsCancellationRequested) OnError?.Invoke($"Mất kết nối: {ex.Message}"); }
            finally { Disconnect(connection); }
        }
        public void Dispose() => Disconnect();
    }
}
