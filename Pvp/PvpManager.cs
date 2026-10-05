using System;
using System.Threading.Tasks;
using sudokuvip.Models;
using sudokuvip.Pvp.Models;
using sudokuvip.Pvp.Network;
using sudokuvip.Services;

namespace sudokuvip.Pvp
{
    public class PvpManager : IDisposable
    {
        private static PvpManager? _instance;
        public static PvpManager Instance => _instance ??= new PvpManager();

        public PvpServer Server { get; } = new();
        public PvpClient Client { get; } = new();

        public bool IsServerHost { get; private set; }
        public PvpPlayer? LocalPlayer { get; private set; }

        public event Action<string>? OnQueueStatus;
        public event Action<PvpRoomInfo>? OnRoomUpdated;
        public event Action<MsgStartMatchPayload>? OnMatchStarted;
        public event Action<MsgProgressPayload>? OnOpponentProgress;
        public event Action<string>? OnOpponentEmote;
        public event Action<PvpMatchResult>? OnMatchOver;
        public event Action<string>? OnError;

        private PvpManager()
        {
            Client.OnMessageReceived += HandleServerMessage;
            Client.OnError += err => OnError?.Invoke(err);
        }

        public async Task<bool> EnsureConnectedAsync(string host = "127.0.0.1", int port = 5123)
        {
            if (Client.IsConnected) return true;

            // Try connecting to existing server
            bool connected = await Client.ConnectAsync(host, port);
            if (!connected)
            {
                // No server found -> Start local embedded server
                if (Server.Start(port))
                {
                    IsServerHost = true;
                    await Task.Delay(100);
                    connected = await Client.ConnectAsync("127.0.0.1", port);
                }
            }

            if (connected)
            {
                var user = AuthService.CurrentUser;
                LocalPlayer = new PvpPlayer
                {
                    Username = user?.Username ?? "guest",
                    DisplayName = string.IsNullOrWhiteSpace(user?.DisplayName) ? (user?.Username ?? "Khách") : user.DisplayName,
                    Avatar = string.IsNullOrWhiteSpace(user?.Avatar) ? "👤" : user.Avatar,
                    EloRating = user?.EloRating ?? 1200
                };
                await Client.SendHello(LocalPlayer);
            }

            return connected;
        }

        private void HandleServerMessage(PvpMessage msg)
        {
            switch (msg.Type)
            {
                case PvpMessageType.QueueStatus:
                    OnQueueStatus?.Invoke(msg.GetPayload<string>() ?? "");
                    break;

                case PvpMessageType.RoomUpdate:
                    var room = msg.GetPayload<PvpRoomInfo>();
                    if (room != null) OnRoomUpdated?.Invoke(room);
                    break;

                case PvpMessageType.StartMatch:
                    var match = msg.GetPayload<MsgStartMatchPayload>();
                    if (match != null) OnMatchStarted?.Invoke(match);
                    break;

                case PvpMessageType.OpponentProgress:
                    var prog = msg.GetPayload<MsgProgressPayload>();
                    if (prog != null) OnOpponentProgress?.Invoke(prog);
                    break;

                case PvpMessageType.OpponentEmote:
                    var emo = msg.GetPayload<MsgEmotePayload>();
                    if (emo != null) OnOpponentEmote?.Invoke(emo.Emote);
                    break;

                case PvpMessageType.MatchOver:
                    var result = msg.GetPayload<PvpMatchResult>();
                    if (result != null)
                    {
                        // Update local user stats if matching
                        UpdateLocalUserStats(result);
                        OnMatchOver?.Invoke(result);
                    }
                    break;

                case PvpMessageType.Error:
                    var err = msg.GetPayload<MsgErrorPayload>();
                    if (err != null) OnError?.Invoke(err.Message);
                    break;
            }
        }

        private void UpdateLocalUserStats(PvpMatchResult result)
        {
            var user = AuthService.CurrentUser;
            if (user == null || LocalPlayer == null) return;

            bool isP1 = result.Player1.Id == LocalPlayer.Id;
            int newElo = isP1 ? result.P1EloAfter : result.P2EloAfter;
            bool won = result.WinnerId == LocalPlayer.Id;

            user.EloRating = newElo;
            user.PvpGames++;
            if (won) user.PvpWins++;
            LocalPlayer.EloRating = newElo;
        }

        public Task JoinQueue(int difficulty) => Client.JoinQueue(difficulty);
        public Task LeaveQueue() => Client.LeaveQueue();
        public Task CreateRoom(int difficulty) => Client.CreateRoom(difficulty);
        public Task JoinRoom(string pin) => Client.JoinRoom(pin);
        public Task LeaveRoom() => Client.LeaveRoom();
        public Task ToggleReady(bool ready) => Client.ToggleReady(ready);
        public Task StartBotMatch(int difficulty) => Client.StartBotMatch(difficulty);
        public Task SendProgress(string matchId, int cellIndex, bool isCorrect, int mistakes, int filled, int totalEmpty, int score) =>
            Client.SendProgress(matchId, cellIndex, isCorrect, mistakes, filled, totalEmpty, score);
        public Task SendEmote(string emote) => Client.SendEmote(emote);
        public Task Surrender(string matchId) => Client.Surrender(matchId);

        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }
}
