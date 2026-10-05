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

        private bool _loading;
        private bool _closed;
        protected override void OnClosed(System.EventArgs e) { _closed = true; base.OnClosed(e); }

        private async void LoadData()
        {
            if (_loading || _closed) return;
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

            _loading = true;
            txtEmptyHistory.Text = "Đang tải lịch sử…";
            txtEmptyHistory.Visibility = Visibility.Visible;
            var result = await AuthService.GetUserHistoryAsync(user);
            _loading = false;
            if (_closed) return;
            if (!result.Success)
            {
                txtEmptyHistory.Text = "Không tải được lịch sử. Kiểm tra kết nối SQL Server và thử làm mới.";
                txtEmptyHistory.Visibility = Visibility.Visible;
                return;
            }

            txtHistoryAvatar.Text = string.IsNullOrEmpty(user.Avatar) ? "👤" : user.Avatar;
            txtPlayerName.Text = string.IsNullOrEmpty(user.DisplayName) ? user.Username : user.DisplayName;
            txtPlayerJoinDate.Text = user.IsGuest ? "Chế độ: Chơi thử (Khách)" : $"Tham gia ngày: {user.CreatedAt:dd/MM/yyyy}";

            txtStatTotalGames.Text = user.TotalGames.ToString();
            txtStatHighScore.Text = user.HighScore.ToString();
            txtStatTotalWins.Text = user.TotalWins.ToString();
            txtStatWinRate.Text = $"{user.WinRate}%";

            var historyList = result.Records;
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
