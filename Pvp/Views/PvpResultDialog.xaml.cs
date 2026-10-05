using System;
using System.Windows;
using System.Windows.Media;
using sudokuvip.Pvp.Models;

namespace sudokuvip.Pvp.Views
{
    public partial class PvpResultDialog : Window
    {
        public PvpResultDialog(PvpMatchResult result, string localPlayerId)
        {
            InitializeComponent();
            PopulateData(result, localPlayerId);
        }

        private void PopulateData(PvpMatchResult result, string localPlayerId)
        {
            bool isP1 = result.Player1.Id == localPlayerId;
            bool isWinner = result.WinnerId == localPlayerId;

            var localStats = isP1 ? result.P1Stats : result.P2Stats;
            var oppStats = isP1 ? result.P2Stats : result.P1Stats;
            var localPlayer = isP1 ? result.Player1 : result.Player2;
            var oppPlayer = isP1 ? result.Player2 : result.Player1;

            int localEloBefore = isP1 ? result.P1EloBefore : result.P2EloBefore;
            int localEloAfter = isP1 ? result.P1EloAfter : result.P2EloAfter;
            int oppEloBefore = isP1 ? result.P2EloBefore : result.P1EloBefore;
            int oppEloAfter = isP1 ? result.P2EloAfter : result.P1EloAfter;

            int localDelta = localEloAfter - localEloBefore;
            int oppDelta = oppEloAfter - oppEloBefore;

            // Set Title & Colors
            if (result.IsDraw)
            {
                txtTitle.Text = "🤝 HOÀ!";
                txtTitle.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36)); // Yellow
            }
            else if (isWinner)
            {
                txtTitle.Text = "🎉 CHIẾN THẮNG!";
                txtTitle.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)); // Green
            }
            else
            {
                txtTitle.Text = "💀 THẤT BẠI!";
                txtTitle.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113)); // Red
            }

            txtReason.Text = result.Reason;

            // Elo updates
            txtP1Name.Text = $"{localPlayer.DisplayName} (Bạn)";
            txtP2Name.Text = oppPlayer.DisplayName;

            string localSign = localDelta >= 0 ? "+" : "";
            txtP1EloChange.Text = $"{localSign}{localDelta} Elo ({localEloAfter})";
            txtP1EloChange.Foreground = localDelta >= 0 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(248, 113, 113));

            string oppSign = oppDelta >= 0 ? "+" : "";
            txtP2EloChange.Text = $"{oppSign}{oppDelta} Elo ({oppEloAfter})";
            txtP2EloChange.Foreground = oppDelta >= 0 ? new SolidColorBrush(Color.FromRgb(52, 211, 153)) : new SolidColorBrush(Color.FromRgb(248, 113, 113));

            // Table values
            txtP1Progress.Text = $"{localStats.FilledCorrect}/{localStats.TotalEmpty} ({localStats.CompletionPercentage}%)";
            txtP2Progress.Text = $"{oppStats.FilledCorrect}/{oppStats.TotalEmpty} ({oppStats.CompletionPercentage}%)";

            txtP1Mistakes.Text = $"{localStats.Mistakes}/3";
            txtP2Mistakes.Text = $"{oppStats.Mistakes}/3";

            txtP1Duration.Text = $"{localStats.DurationSeconds / 60:D2}:{localStats.DurationSeconds % 60:D2}";
            txtP2Duration.Text = $"{oppStats.DurationSeconds / 60:D2}:{oppStats.DurationSeconds % 60:D2}";

            txtP1Score.Text = localStats.Score.ToString();
            txtP2Score.Text = oppStats.Score.ToString();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
