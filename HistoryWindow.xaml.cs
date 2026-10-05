using System.Windows;
using sudokuvip.Services;

namespace sudokuvip
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
            LoadData();
        }

        private void LoadData()
        {
            var user = AuthService.CurrentUser;
            if (user == null)
            {
                txtHistoryAvatar.Text = "👤";
                txtPlayerName.Text = "Khách (Chưa đăng nhập)";
                txtPlayerJoinDate.Text = "";
                txtStatTotalGames.Text = "0";
                txtStatHighScore.Text = "0";
                txtStatTotalWins.Text = "0";
                txtStatWinRate.Text = "0%";
                txtEmptyHistory.Visibility = Visibility.Visible;
                return;
            }

            AuthService.RefreshCurrentUser();

            txtHistoryAvatar.Text = string.IsNullOrEmpty(user.Avatar) ? "👤" : user.Avatar;
            txtPlayerName.Text = string.IsNullOrEmpty(user.DisplayName) ? user.Username : user.DisplayName;
            txtPlayerJoinDate.Text = user.IsGuest ? "Chế độ: Chơi thử (Khách)" : $"Tham gia ngày: {user.CreatedAt:dd/MM/yyyy}";

            txtStatTotalGames.Text = user.TotalGames.ToString();
            txtStatHighScore.Text = user.HighScore.ToString();
            txtStatTotalWins.Text = user.TotalWins.ToString();
            txtStatWinRate.Text = $"{user.WinRate}%";

            var historyList = AuthService.GetUserHistory(user.IsGuest ? 0 : user.UserId);
            dgHistory.ItemsSource = historyList;

            if (historyList.Count == 0)
            {
                txtEmptyHistory.Text = user.IsGuest 
                    ? "Chưa có ván đấu nào trong phiên Khách này. Hãy chơi một ván ngay nhé!" 
                    : "Chưa có ván đấu nào được ghi nhận. Hãy hoàn thành ván đầu tiên nhé!";
                txtEmptyHistory.Visibility = Visibility.Visible;
            }
            else
            {
                txtEmptyHistory.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadData();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
