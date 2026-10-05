using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using sudokuvip.Services;

namespace sudokuvip
{
    public partial class LoginWindow : Window
    {
        public bool IsAuthenticated { get; private set; } = false;

        private bool _authBusy;
        private bool _closed;
        private System.Threading.CancellationTokenSource? _usernameCheck;
        private long _usernameVersion;

        private void SetBusy(bool busy)
        {
            _authBusy = busy;
            btnLogin.IsEnabled = btnRegister.IsEnabled = btnConfirmGuest.IsEnabled = !busy;
            btnTabLogin.IsEnabled = btnTabRegister.IsEnabled = btnTabGuest.IsEnabled = !busy;
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // An authentication operation must settle before the dialog can change identity.
            if (_authBusy) e.Cancel = true;
            base.OnClosing(e);
        }
        protected override void OnClosed(EventArgs e)
        {
            _closed = true;
            _usernameCheck?.Cancel(); _usernameCheck?.Dispose();
            base.OnClosed(e);
        }

        private string _selectedRegisterAvatar = "👤";
        private string _selectedGuestAvatar = "👤";
        private bool _isLoginPassVisible = false;
        private bool _isRegPassVisible = false;

        public LoginWindow()
        {
            InitializeComponent();
            HighlightSelectedAvatar(wpAvatars, _selectedRegisterAvatar);
            HighlightSelectedAvatar(wpGuestAvatars, _selectedGuestAvatar);

            txtLoginUser.MaxLength = txtRegUser.MaxLength = 50;
            txtRegDisplayName.MaxLength = txtGuestNickname.MaxLength = 100;
            txtRegPass.MaxLength = txtRegPassVisible.MaxLength = txtRegConfirmPass.MaxLength = 128;
            txtGuestNickname.Text = AuthService.GenerateGuestName();

            // Nếu người chơi trước đó là Khách và đã có ván chơi / điểm số, hiển thị tùy chọn giữ lại thành tích
            if (AuthService.CurrentUser != null && AuthService.CurrentUser.IsGuest && 
                (AuthService.CurrentUser.TotalGames > 0 || AuthService.CurrentUser.TotalScore > 0))
            {
                chkMigrateGuest.Visibility = Visibility.Visible;
                chkMigrateGuest.IsChecked = true;
                chkMigrateGuest.Content = $"Chuyển {AuthService.CurrentUser.TotalScore} điểm & {AuthService.CurrentUser.TotalGames} ván chơi Khách sang tài khoản này";
            }

            txtLoginUser.Focus();
        }

        #region TAB SWITCHING

        private void BtnTabLogin_Click(object sender, RoutedEventArgs e)
        {
            panelLogin.Visibility = Visibility.Visible;
            panelRegister.Visibility = Visibility.Collapsed;
            panelGuest.Visibility = Visibility.Collapsed;

            btnTabLogin.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250)); // #60A5FA
            btnTabRegister.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
            btnTabGuest.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));

            lblLoginMessage.Text = "";
            txtLoginUser.Focus();
        }

        private void BtnTabRegister_Click(object sender, RoutedEventArgs e)
        {
            panelLogin.Visibility = Visibility.Collapsed;
            panelRegister.Visibility = Visibility.Visible;
            panelGuest.Visibility = Visibility.Collapsed;

            btnTabRegister.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            btnTabLogin.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
            btnTabGuest.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));

            lblRegMessage.Text = "";
            txtRegUser.Focus();
        }

        private void BtnTabGuest_Click(object sender, RoutedEventArgs e)
        {
            panelLogin.Visibility = Visibility.Collapsed;
            panelRegister.Visibility = Visibility.Collapsed;
            panelGuest.Visibility = Visibility.Visible;

            btnTabGuest.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // #F59E0B
            btnTabLogin.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));
            btnTabRegister.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));

            txtGuestNickname.Focus();
        }

        #endregion

        #region AVATAR SELECTION

        private void Avatar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content != null)
            {
                _selectedRegisterAvatar = btn.Content.ToString() ?? "👤";
                HighlightSelectedAvatar(wpAvatars, _selectedRegisterAvatar);
            }
        }

        private void GuestAvatar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content != null)
            {
                _selectedGuestAvatar = btn.Content.ToString() ?? "👤";
                HighlightSelectedAvatar(wpGuestAvatars, _selectedGuestAvatar);
            }
        }

        private void HighlightSelectedAvatar(Panel panel, string selectedAvatar)
        {
            foreach (var child in panel.Children)
            {
                if (child is Button btn)
                {
                    bool isSelected = btn.Content?.ToString() == selectedAvatar;
                    btn.BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(96, 165, 250)) : new SolidColorBrush(Color.FromRgb(51, 51, 51));
                    btn.BorderThickness = new Thickness(isSelected ? 2 : 1);
                    btn.Background = isSelected ? new SolidColorBrush(Color.FromRgb(30, 41, 59)) : new SolidColorBrush(Color.FromRgb(34, 34, 34));
                }
            }
        }

        #endregion

        #region PASSWORD VISIBILITY & STRENGTH

        private void BtnToggleLoginPass_Click(object sender, RoutedEventArgs e)
        {
            _isLoginPassVisible = !_isLoginPassVisible;
            if (_isLoginPassVisible)
            {
                txtLoginPassVisible.Text = txtLoginPass.Password;
                txtLoginPassVisible.Visibility = Visibility.Visible;
                txtLoginPass.Visibility = Visibility.Collapsed;
                btnToggleLoginPass.Content = "🔒";
            }
            else
            {
                txtLoginPass.Password = txtLoginPassVisible.Text;
                txtLoginPass.Visibility = Visibility.Visible;
                txtLoginPassVisible.Visibility = Visibility.Collapsed;
                btnToggleLoginPass.Content = "👁️";
            }
        }

        private void BtnToggleRegPass_Click(object sender, RoutedEventArgs e)
        {
            _isRegPassVisible = !_isRegPassVisible;
            if (_isRegPassVisible)
            {
                txtRegPassVisible.Text = txtRegPass.Password;
                txtRegPassVisible.Visibility = Visibility.Visible;
                txtRegPass.Visibility = Visibility.Collapsed;
                btnToggleRegPass.Content = "🔒";
            }
            else
            {
                txtRegPass.Password = txtRegPassVisible.Text;
                txtRegPass.Visibility = Visibility.Visible;
                txtRegPassVisible.Visibility = Visibility.Collapsed;
                btnToggleRegPass.Content = "👁️";
            }
        }

        private void TxtRegPass_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (!_isRegPassVisible)
            {
                UpdatePasswordStrength(txtRegPass.Password);
            }
        }

        private void TxtRegPassVisible_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isRegPassVisible)
            {
                UpdatePasswordStrength(txtRegPassVisible.Text);
            }
        }

        private void UpdatePasswordStrength(string pass)
        {
            if (string.IsNullOrEmpty(pass))
            {
                pbPassStrength.Value = 0;
                lblPassStrength.Text = "";
                return;
            }

            int score = 0;
            if (pass.Length >= 6) score += 30;
            if (pass.Length >= 8) score += 20;
            if (pass.Any(char.IsUpper) && pass.Any(char.IsLower)) score += 25;
            if (pass.Any(char.IsDigit)) score += 15;
            if (pass.Any(ch => !char.IsLetterOrDigit(ch))) score += 10;

            score = Math.Min(100, score);
            pbPassStrength.Value = score;

            if (score < 40)
            {
                pbPassStrength.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Đỏ
                lblPassStrength.Text = "Độ mạnh: Yếu";
                lblPassStrength.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
            }
            else if (score < 75)
            {
                pbPassStrength.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11)); // Vàng
                lblPassStrength.Text = "Độ mạnh: Trung bình";
                lblPassStrength.Foreground = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            }
            else
            {
                pbPassStrength.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129)); // Xanh lá
                lblPassStrength.Text = "Độ mạnh: Rất an toàn ✓";
                lblPassStrength.Foreground = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }
        }

        private async void TxtRegUser_TextChanged(object sender, TextChangedEventArgs e)
        {
            long version = ++_usernameVersion;
            _usernameCheck?.Cancel(); _usernameCheck?.Dispose();
            var check = _usernameCheck = new System.Threading.CancellationTokenSource();
            string username = txtRegUser.Text.Trim();
            if (lblUserCheck == null) return;
            if (username.Length == 0) { lblUserCheck.Text = ""; return; }
            if (!AuthService.ValidUsername(username))
            {
                lblUserCheck.Text = "⚠️ Cần 3–50 ký tự: chữ, số hoặc _";
                lblUserCheck.Foreground = Brushes.Orange;
                return;
            }
            lblUserCheck.Text = "Đang kiểm tra tên đăng nhập…";
            lblUserCheck.Foreground = Brushes.Gray;
            try
            {
                await System.Threading.Tasks.Task.Delay(350,check.Token);
                var status = await AuthService.CheckUsernameExistsAsync(username,check.Token);
                if (_closed || version != _usernameVersion) return;
                lblUserCheck.Text = status switch
                {
                    UsernameStatus.Available => "✓ Tên đăng nhập khả dụng",
                    UsernameStatus.Exists => "❌ Tên đăng nhập đã được sử dụng",
                    _ => "⚠️ Không thể kiểm tra tên: kiểm tra kết nối SQL Server"
                };
                lblUserCheck.Foreground = status == UsernameStatus.Available ? Brushes.SeaGreen : Brushes.OrangeRed;
            }
            catch (OperationCanceledException) { }
        }

        #endregion

        #region ACTIONS: LOGIN, REGISTER, GUEST

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_authBusy) return;
            string username = txtLoginUser.Text.Trim();
            string password = _isLoginPassVisible ? txtLoginPassVisible.Text : txtLoginPass.Password;

            lblLoginMessage.Text = "";

            SetBusy(true);
            lblLoginMessage.Text = "Đang đăng nhập…";
            var result = await AuthService.LoginAsync(username, password);
            SetBusy(false);
            if (_closed) return;
            if (result.Success)
            {
                IsAuthenticated = true;
                DialogResult = true;
                Close();
            }
            else
            {
                lblLoginMessage.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                lblLoginMessage.Text = result.Message;
            }
        }

        private async void BtnRegister_Click(object sender, RoutedEventArgs e)
        {
            if (_authBusy) return;
            string username = txtRegUser.Text.Trim();
            string displayName = txtRegDisplayName.Text.Trim();
            string password = _isRegPassVisible ? txtRegPassVisible.Text : txtRegPass.Password;
            string confirmPass = txtRegConfirmPass.Password;

            lblRegMessage.Text = "";

            if (password != confirmPass)
            {
                lblRegMessage.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                lblRegMessage.Text = "Mật khẩu xác nhận không khớp!";
                return;
            }

            bool migrateGuest = chkMigrateGuest.IsChecked == true;

            SetBusy(true);
            lblRegMessage.Text = "Đang tạo tài khoản…";
            var result = await AuthService.RegisterAsync(username, password, displayName, _selectedRegisterAvatar, migrateGuest);
            SetBusy(false);
            if (_closed) return;
            if (result.Success)
            {
                string note = migrateGuest ? "\n(Đã chuyển toàn bộ điểm số từ phiên Khách vào tài khoản!)" : "";
                MessageBox.Show($"Chúc mừng {result.User?.DisplayName}! Tài khoản đã được tạo thành công trên SQL Server.{note}", 
                    "Đăng ký thành công", MessageBoxButton.OK, MessageBoxImage.Information);

                IsAuthenticated = true;
                DialogResult = true;
                Close();
            }
            else
            {
                lblRegMessage.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                lblRegMessage.Text = result.Message;
            }
        }

        private void BtnRandomGuestName_Click(object sender, RoutedEventArgs e)
        {
            txtGuestNickname.Text = AuthService.GenerateGuestName();
        }

        private void BtnConfirmGuest_Click(object sender, RoutedEventArgs e)
        {
            if (_authBusy) return;
            string nickname = txtGuestNickname.Text.Trim();
            AuthService.LoginAsGuest(nickname, _selectedGuestAvatar);

            IsAuthenticated = true;
            DialogResult = true;
            Close();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (panelLogin.Visibility == Visibility.Visible)
                {
                    BtnLogin_Click(sender, new RoutedEventArgs());
                }
                else if (panelRegister.Visibility == Visibility.Visible)
                {
                    BtnRegister_Click(sender, new RoutedEventArgs());
                }
                else if (panelGuest.Visibility == Visibility.Visible)
                {
                    BtnConfirmGuest_Click(sender, new RoutedEventArgs());
                }
            }
        }

        #endregion
    }
}
