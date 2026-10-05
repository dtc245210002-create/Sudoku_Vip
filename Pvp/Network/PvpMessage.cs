using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using sudokuvip.Pvp.Models;

namespace sudokuvip.Pvp.Network
{
    public enum PvpMessageType
    {
        Hello,
        HelloAck,
        JoinQueue,
        QueueStatus,
        LeaveQueue,
        CreateRoom,
        JoinRoom,
        RoomUpdate,
        LeaveRoom,
        ToggleReady,
        StartBotMatch,
        StartMatch,
        PlayerProgress,
        OpponentProgress,
        SendEmote,
        OpponentEmote,
        Surrender,
        MatchOver,
        RequestRematch,
        RematchStatus,
        Error
    }

    public class PvpMessage
    {
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public PvpMessageType Type { get; set; }
        public string? Payload { get; set; }

        public static PvpMessage Create<T>(PvpMessageType type, T payload)
        {
            return new PvpMessage
            {
                Type = type,
                Payload = JsonSerializer.Serialize(payload)
            };
        }

        public static PvpMessage Create(PvpMessageType type)
        {
            return new PvpMessage { Type = type };
        }

        public T? GetPayload<T>()
        {
            if (string.IsNullOrEmpty(Payload)) return default;
            try
            {
                return JsonSerializer.Deserialize<T>(Payload);
            }
            catch
            {
                return default;
            }
        }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static PvpMessage? FromJson(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<PvpMessage>(json);
            }
            catch
            {
                return null;
            }
        }
    }

    // Payload definitions
    public class MsgJoinQueuePayload
    {
        public int Difficulty { get; set; }
    }

    public class MsgCreateRoomPayload
    {
        public int Difficulty { get; set; }
    }

    public class MsgJoinRoomPayload
    {
        public string RoomPin { get; set; } = string.Empty;
    }

    public class MsgStartMatchPayload
    {
        public string MatchId { get; set; } = string.Empty;
        public PvpPlayer Opponent { get; set; } = new();
        public PvpBoardData Board { get; set; } = new();
        public int Difficulty { get; set; }
    }

    public class MsgProgressPayload
    {
        public string MatchId { get; set; } = string.Empty;
        public int CellIndex { get; set; }
        public bool IsCorrect { get; set; }
        public int Mistakes { get; set; }
        public int FilledCorrect { get; set; }
        public int TotalEmpty { get; set; }
        public int Score { get; set; }
    }

    public class MsgEmotePayload
    {
        public string Emote { get; set; } = string.Empty;
    }

    public class MsgRematchStatusPayload
    {
        public bool Player1Wants { get; set; }
        public bool Player2Wants { get; set; }
    }

    public class MsgErrorPayload
    {
        public string Message { get; set; } = string.Empty;
    }
}
