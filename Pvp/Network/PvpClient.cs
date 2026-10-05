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
        private TcpClient? _tcpClient;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public bool IsConnected => _tcpClient?.Connected == true;
        public PvpPlayer? CurrentPlayer { get; private set; }

        public event Action? OnConnected;
        public event Action? OnDisconnected;
        public event Action<PvpMessage>? OnMessageReceived;
        public event Action<string>? OnError;

        public async Task<bool> ConnectAsync(string host = "127.0.0.1", int port = 5123)
        {
            try
            {
                Disconnect();
                _cts = new CancellationTokenSource();
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(host, port);
                var stream = _tcpClient.GetStream();
                _reader = new StreamReader(stream, Encoding.UTF8);
                _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                OnConnected?.Invoke();
                _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
                return true;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Không thể kết nối máy chủ: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            _cts?.Cancel();
            try { _tcpClient?.Close(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }
            try { _tcpClient?.Dispose(); } catch { }
            _reader = null;
            _writer = null;
            _tcpClient = null;
            OnDisconnected?.Invoke();
        }

        public async Task SendAsync(PvpMessage message)
        {
            if (_writer == null || !IsConnected) return;
            string json = message.ToJson();
            await _sendLock.WaitAsync();
            try
            {
                await _writer.WriteLineAsync(json);
                await _writer.FlushAsync();
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Lỗi gửi gói tin: {ex.Message}");
            }
            finally
            {
                _sendLock.Release();
            }
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

        private async Task ReceiveLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _reader != null)
                {
                    string? line = await _reader.ReadLineAsync(token);
                    if (line == null) break; // Disconnected by server
                    var msg = PvpMessage.FromJson(line);
                    if (msg != null)
                    {
                        OnMessageReceived?.Invoke(msg);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                OnError?.Invoke($"Mất kết nối với máy chủ: {ex.Message}");
            }
            finally
            {
                Disconnect();
            }
        }

        public void Dispose()
        {
            Disconnect();
            _sendLock.Dispose();
        }
    }
}
