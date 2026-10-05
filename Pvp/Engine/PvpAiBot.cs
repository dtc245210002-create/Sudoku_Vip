using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using sudokuvip.Pvp.Models;

namespace sudokuvip.Pvp.Engine
{
    public class PvpAiBot
    {
        public PvpPlayer PlayerInfo { get; }
        private readonly PvpBoardData _board;
        private readonly int _difficulty;
        private readonly CancellationTokenSource _cts = new();

        public event Action<int, bool, int, int, int>? OnProgressUpdated; // cellIndex, isCorrect, mistakes, filledCount, score
        public event Action<string>? OnEmoteSent;

        private int _mistakes;
        private int _filledCorrect;
        private int _score;
        private readonly bool[] _solved;
        private readonly Random _random = new();

        public PvpAiBot(PvpBoardData board, int difficulty, int playerElo = 1200)
        {
            _board = board;
            _difficulty = difficulty;
            _solved = new bool[81];

            // Mark given clues as already solved
            for (int i = 0; i < 81; i++)
            {
                if (_board.Clues[i] > 0)
                {
                    _solved[i] = true;
                }
            }

            int botElo = Math.Max(800, playerElo + _random.Next(-50, 60));
            string[] names = { "SudokuMaster_AI", "CyberSolver_99", "AlphaDoku", "Bot_Kuro", "Nexus_Doku" };
            string[] avatars = { "🤖", "🦊", "⚡", "🎮", "👾" };

            PlayerInfo = new PvpPlayer
            {
                Id = "bot_" + Guid.NewGuid().ToString("N")[..8],
                Username = "bot_ai",
                DisplayName = names[_random.Next(names.Length)],
                Avatar = avatars[_random.Next(avatars.Length)],
                EloRating = botElo,
                IsReady = true,
                IsBot = true
            };
        }

        public void StartPlaying()
        {
            Task.Run(() => PlayLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        public void HandlePlayerEmote(string playerEmote)
        {
            if (_random.NextDouble() < 0.6) // 60% chance to respond
            {
                Task.Delay(_random.Next(1200, 2500)).ContinueWith(_ =>
                {
                    if (!_cts.IsCancellationRequested)
                    {
                        string[] responses = playerEmote switch
                        {
                            "👋" => new[] { "👋", "😎" },
                            "😎" => new[] { "🔥", "😎", "💪" },
                            "😱" => new[] { "😎", "😆", "🔥" },
                            "🔥" => new[] { "🔥", "⚡", "😱" },
                            "GG" => new[] { "GG", "👏" },
                            _ => new[] { "😎", "🔥" }
                        };
                        string pick = responses[_random.Next(responses.Length)];
                        OnEmoteSent?.Invoke(pick);
                    }
                });
            }
        }

        private async Task PlayLoopAsync(CancellationToken token)
        {
            // Initial delay before first move (1.5 - 3s)
            await Task.Delay(_random.Next(1500, 3000), token);

            while (!token.IsCancellationRequested)
            {
                // Find all unfilled cells
                var emptyIndices = new List<int>();
                for (int i = 0; i < 81; i++)
                {
                    if (!_solved[i]) emptyIndices.Add(i);
                }

                if (emptyIndices.Count == 0 || _mistakes >= 3)
                {
                    break;
                }

                // Choose delay based on difficulty
                // Easy: 4000 - 8000 ms, Medium: 3000 - 6500 ms, Hard: 2500 - 5000 ms, Expert: 2000 - 4500 ms
                int minDelay = _difficulty switch { 0 => 4000, 1 => 3000, 2 => 2500, _ => 2000 };
                int maxDelay = _difficulty switch { 0 => 8000, 1 => 6500, 2 => 5000, _ => 4500 };
                int delay = _random.Next(minDelay, maxDelay);

                try
                {
                    await Task.Delay(delay, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Small chance of making a mistake (e.g. 7% on easy, 4% on hard)
                double mistakeChance = _difficulty == 0 ? 0.07 : 0.04;
                int chosenIndex = emptyIndices[_random.Next(emptyIndices.Count)];

                if (_random.NextDouble() < mistakeChance && _mistakes < 2)
                {
                    _mistakes++;
                    OnProgressUpdated?.Invoke(chosenIndex, false, _mistakes, _filledCorrect, _score);

                    // Send panic emote occasionally on mistake
                    if (_random.NextDouble() < 0.4)
                    {
                        await Task.Delay(600, token);
                        OnEmoteSent?.Invoke("😱");
                    }
                }
                else
                {
                    _solved[chosenIndex] = true;
                    _filledCorrect++;
                    _score += 10;
                    OnProgressUpdated?.Invoke(chosenIndex, true, _mistakes, _filledCorrect, _score);
                }

                // Chance to send hype emote when completing milestones
                if (_filledCorrect > 0 && _filledCorrect % 15 == 0 && _random.NextDouble() < 0.5)
                {
                    await Task.Delay(500, token);
                    OnEmoteSent?.Invoke("🔥");
                }
            }
        }
    }
}
