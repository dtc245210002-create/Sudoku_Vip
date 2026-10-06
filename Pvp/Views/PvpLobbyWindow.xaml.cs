using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using sudokuvip.Pvp.Models;
using sudokuvip.Pvp.Network;
using sudokuvip.Services;

namespace sudokuvip.Pvp.Views
{
    public partial class PvpLobbyWindow : Window
    {
        private readonly PvpManager _manager = PvpManager.Instance;
        private readonly DispatcherTimer _searchTimer;
        private int _searchSeconds = 0;
        private PvpRoomInfo? _currentRoom;
        private bool _isLocalReady = false;
        private bool _closed;
        private bool _connecting;
        private bool _inArena;

        public PvpLobbyWindow()
        {
            InitializeComponent();

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _searchTimer.Tick += SearchTimer_Tick;

            UpdateUserProfileUI();
            if (_manager.IsReady)
            {
                txtServerHost.Text=_manager.ServerHost; txtServerPort.Text=_manager.ServerPort.ToString();
                txtConnectionStatus.Text=$"Đã kết nối {_manager.ServerHost}:{_manager.ServerPort}.";
                MatchOptions.IsEnabled=true;
            }
            WireEvents();

            Loaded += (s,e) => { MatchOptions.IsEnabled=_manager.IsReady; UpdateUserProfileUI(); };
        }
        private bool ReadPort(out int port)
        {
            if (int.TryParse(txtServerPort.Text,out port) && port is >0 and <=65535) return true;
            txtConnectionStatus.Text="Cổng phải là số từ 1 đến 65535."; return false;
        }
        private async System.Threading.Tasks.Task ConnectAsync(bool hostServer,bool lan)
        {
            if (_closed || _connecting || _inArena || !ReadPort(out int port)) return;
            string host=txtServerHost.Text.Trim();
            if (!hostServer && host.Length==0) { txtConnectionStatus.Text="Nhập địa chỉ máy chủ."; return; }
            _connecting=true; MatchOptions.IsEnabled=false;
            btnConnect.IsEnabled=btnHostLan.IsEnabled=btnHostLocal.IsEnabled=false;
            txtConnectionStatus.Text="Đang kết nối…";
            try
            {
                if (hostServer)
                {
                    if (_manager.Server.IsRunning && (_manager.Server.Port!=port || (lan && !_manager.Server.ListenAddress.Equals(System.Net.IPAddress.Any))))
                    { txtConnectionStatus.Text="Máy chủ đang chạy với cấu hình khác; đóng ứng dụng trước khi đổi cổng hoặc bật LAN."; return; }
                    if (!_manager.Server.IsRunning && !_manager.Server.Start(port,lan ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback))
                    { txtConnectionStatus.Text="Không tạo được máy chủ; cổng có thể đang được sử dụng."; return; }
                    host="127.0.0.1"; txtServerHost.Text=host;
                }
                bool connected=await _manager.EnsureConnectedAsync(host,port);
                if (_closed) { _manager.LeaveLobby(); return; }
                MatchOptions.IsEnabled=connected;
                txtConnectionStatus.Text=connected ? (lan ? $"Máy chủ LAN: cổng {port}. Máy khác nhập IP của máy này và cùng cổng." : $"Đã kết nối {host}:{port}.") : "Không kết nối được. Kiểm tra địa chỉ/cổng hoặc chọn tạo máy chủ.";
                UpdateUserProfileUI();
                if (connected && hostServer && !lan) BtnPracticeAi_Click(this,new RoutedEventArgs());
            }
            finally
            {
                _connecting=false;
                if (!_closed) btnConnect.IsEnabled=btnHostLan.IsEnabled=btnHostLocal.IsEnabled=true;
            }
        }
        private async void BtnConnect_Click(object sender,RoutedEventArgs e) => await ConnectAsync(false,false);
        private async void BtnHostLan_Click(object sender,RoutedEventArgs e) => await ConnectAsync(true,true);
        private async void BtnHostLocal_Click(object sender,RoutedEventArgs e) => await ConnectAsync(true,false);
        private async void BtnRetrySave_Click(object sender,RoutedEventArgs e)
        { await _manager.RetryPendingResultsAsync(); if (!_closed) UpdateUserProfileUI(); }
        private void HandleDisconnected() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_closed) return;
            MatchOptions.IsEnabled=false; _searchTimer.Stop();
            borderSearching.Visibility=Visibility.Collapsed; btnFindMatch.Visibility=Visibility.Visible; btnCancelFindMatch.Visibility=Visibility.Collapsed;
            _currentRoom=null; subPanelInRoom.Visibility=Visibility.Collapsed; subPanelRoomEntry.Visibility=Visibility.Visible;
            txtConnectionStatus.Text="Đã ngắt kết nối. Kết nối lại để tiếp tục.";
        }));

        private void WireEvents()
        {
            _manager.OnQueueStatus += HandleQueueStatus;
            _manager.OnRoomUpdated += HandleRoomUpdated;
            _manager.OnMatchStarted += HandleMatchStarted;
            _manager.OnError += HandleError;
            _manager.OnDisconnected += HandleDisconnected;
            AuthService.CurrentUserChanged += HandleProfileChanged;
        }

        private void UnwireEvents()
        {
            _manager.OnQueueStatus -= HandleQueueStatus;
            _manager.OnRoomUpdated -= HandleRoomUpdated;
            _manager.OnMatchStarted -= HandleMatchStarted;
            _manager.OnError -= HandleError;
            _manager.OnDisconnected -= HandleDisconnected;
            AuthService.CurrentUserChanged -= HandleProfileChanged;
        }

        private void HandleProfileChanged() => Dispatcher.BeginInvoke(new Action(() => { if (!_closed) UpdateUserProfileUI(); }));

        private void UpdateUserProfileUI()
        {
            var user = AuthService.CurrentUser;
            if (user != null)
            {
                txtUserAvatar.Text = string.IsNullOrEmpty(user.Avatar) ? "👤" : user.Avatar;
                txtUserDisplayName.Text = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
                txtUserElo.Text = $"{user.EloRating} Elo";
                txtUserWinRate.Text = $"PvP Thắng: {user.PvpWinRate}% ({user.PvpWins}/{user.PvpGames})";
            }
        }

        #region TAB SWITCHING

        private void BtnTabQuickMatch_Click(object sender, RoutedEventArgs e)
        {
            panelQuickMatch.Visibility = Visibility.Visible;
            panelCustomRoom.Visibility = Visibility.Collapsed;

            btnTabQuickMatch.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            btnTabQuickMatch.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));

            btnTabCustomRoom.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            btnTabCustomRoom.BorderBrush = Brushes.Transparent;
        }

        private void BtnTabCustomRoom_Click(object sender, RoutedEventArgs e)
        {
            panelQuickMatch.Visibility = Visibility.Collapsed;
            panelCustomRoom.Visibility = Visibility.Visible;

            btnTabCustomRoom.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
            btnTabCustomRoom.BorderBrush = new SolidColorBrush(Color.FromRgb(59, 130, 246));

            btnTabQuickMatch.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
            btnTabQuickMatch.BorderBrush = Brushes.Transparent;
        }

        #endregion

        #region TAB 1: QUICK MATCH & BOT MATCH

        private int GetSelectedDifficulty()
        {
            if (rbDiffEasy.IsChecked == true) return 0;
            if (rbDiffMedium.IsChecked == true) return 1;
            if (rbDiffHard.IsChecked == true) return 2;
            if (rbDiffExpert.IsChecked == true) return 3;
            return 0;
        }

        private void BtnFindMatch_Click(object sender, RoutedEventArgs e)
        {
            int diff = GetSelectedDifficulty();
            _searchSeconds = 0;
            txtSearchTimer.Text = "Thời gian tìm: 00:00";
            borderSearching.Visibility = Visibility.Visible;
            btnFindMatch.Visibility = Visibility.Collapsed;
            btnCancelFindMatch.Visibility = Visibility.Visible;
            _searchTimer.Start();

            _ = _manager.JoinQueue(diff);
        }

        private void BtnCancelFindMatch_Click(object sender, RoutedEventArgs e)
        {
            _searchTimer.Stop();
            borderSearching.Visibility = Visibility.Collapsed;
            btnFindMatch.Visibility = Visibility.Visible;
            btnCancelFindMatch.Visibility = Visibility.Collapsed;

            _ = _manager.LeaveQueue();
        }

        private void SearchTimer_Tick(object? sender, EventArgs e)
        {
            _searchSeconds++;
            txtSearchTimer.Text = $"Thời gian tìm: {_searchSeconds / 60:D2}:{_searchSeconds % 60:D2}";
        }

        private void BtnPracticeAi_Click(object sender, RoutedEventArgs e)
        {
            int diff = GetSelectedDifficulty();
            _ = _manager.StartBotMatch(diff);
        }

        private void HandleQueueStatus(string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                txtConnectionStatus.Text=status;
            }));
        }

        #endregion

        #region TAB 2: CUSTOM ROOM

        private void BtnCreateRoom_Click(object sender, RoutedEventArgs e)
        {
            int diff = cbRoomDifficulty.SelectedIndex;
            _ = _manager.CreateRoom(diff);
        }

        private void BtnJoinRoom_Click(object sender, RoutedEventArgs e)
        {
            string pin = txtRoomPinInput.Text.Trim();
            if (string.IsNullOrEmpty(pin))
            {
                MessageBox.Show("Vui lòng nhập mã PIN phòng.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _ = _manager.JoinRoom(pin);
        }

        private void HandleRoomUpdated(PvpRoomInfo room)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed || _inArena) return;
                _currentRoom = room;
                subPanelRoomEntry.Visibility = Visibility.Collapsed;
                subPanelInRoom.Visibility = Visibility.Visible;

                txtRoomPinDisplay.Text = $"#{room.RoomPin}";
                txtRoomDifficultyDisplay.Text = room.Difficulty switch
                {
                    0 => "(Độ khó: Dễ)",
                    1 => "(Độ khó: Trung bình)",
                    2 => "(Độ khó: Khó)",
                    3 => "(Độ khó: Chuyên gia)",
                    _ => "(Độ khó: Trung bình)"
                };

                // Update Slot 1 (Host)
                if (room.Player1 != null)
                {
                    txtSlot1Avatar.Text = room.Player1.Avatar;
                    txtSlot1Name.Text = room.Player1.DisplayName;
                    txtSlot1Elo.Text = $"{room.Player1.EloRating} Elo";
                    txtSlot1Ready.Text = room.Player1.IsReady ? "✓ SẴN SÀNG" : "CHƯA SẴN SÀNG";
                    borderSlot1Ready.Background = room.Player1.IsReady ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(55, 65, 81));
                }

                // Update Slot 2 (Guest)
                if (room.Player2 != null)
                {
                    txtSlot2Avatar.Text = room.Player2.Avatar;
                    txtSlot2Name.Text = room.Player2.DisplayName;
                    txtSlot2Elo.Text = $"{room.Player2.EloRating} Elo";
                    txtSlot2Ready.Text = room.Player2.IsReady ? "✓ SẴN SÀNG" : "CHƯA SẴN SÀNG";
                    borderSlot2Ready.Background = room.Player2.IsReady ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(55, 65, 81));
                }
                else
                {
                    txtSlot2Avatar.Text = "❓";
                    txtSlot2Name.Text = "Đang đợi người chơi...";
                    txtSlot2Elo.Text = "--- Elo";
                    txtSlot2Ready.Text = "CHƯA SẴN SÀNG";
                    borderSlot2Ready.Background = new SolidColorBrush(Color.FromRgb(55, 65, 81));
                }

                // Check local player ready state
                var localId = _manager.LocalPlayer?.Id;
                bool isLocalReady = (room.Player1?.Id == localId && room.Player1?.IsReady == true) ||
                                    (room.Player2?.Id == localId && room.Player2?.IsReady == true);
                _isLocalReady = isLocalReady;

                btnToggleReady.Content = _isLocalReady ? "Hủy Sẵn Sàng" : "Sẵn Sàng!";
                btnToggleReady.Background = _isLocalReady ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(16, 185, 129));
            }));
        }

        private void BtnToggleReady_Click(object sender, RoutedEventArgs e)
        {
            _isLocalReady = !_isLocalReady;
            _ = _manager.ToggleReady(_isLocalReady);
        }

        private void BtnLeaveRoom_Click(object sender, RoutedEventArgs e)
        {
            _ = _manager.LeaveRoom();
            subPanelInRoom.Visibility = Visibility.Collapsed;
            subPanelRoomEntry.Visibility = Visibility.Visible;
            _currentRoom = null;
        }

        #endregion

        #region MATCH START & ERROR

        private void HandleMatchStarted(MsgStartMatchPayload match)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed || _inArena) { _ = _manager.Surrender(match.MatchId); return; }
                _inArena=true;
                _searchTimer.Stop();
                borderSearching.Visibility = Visibility.Collapsed;
                btnFindMatch.Visibility = Visibility.Visible;
                btnCancelFindMatch.Visibility = Visibility.Collapsed;

                var arena = new PvpArenaWindow(match)
                {
                    Owner = this
                };

                Hide();
                arena.ShowDialog();
                _inArena=false;
                if (_closed) return;
                Show();
                UpdateUserProfileUI();
                _ = _manager.LeaveRoom();

                // If was in custom room, reset room view
                if (_currentRoom != null)
                {
                    subPanelInRoom.Visibility = Visibility.Collapsed;
                    subPanelRoomEntry.Visibility = Visibility.Visible;
                    _currentRoom = null;
                }
            }));
        }

        private void HandleError(string err)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_closed) return;
                txtConnectionStatus.Text=err;
                _searchTimer.Stop();
                borderSearching.Visibility=Visibility.Collapsed; btnFindMatch.Visibility=Visibility.Visible; btnCancelFindMatch.Visibility=Visibility.Collapsed;
            }));
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            _closed=true;
            _searchTimer.Stop();
            UnwireEvents();
            _manager.LeaveLobby();
            base.OnClosed(e);
        }
    }
}
