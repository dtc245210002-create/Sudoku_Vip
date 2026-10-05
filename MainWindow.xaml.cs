using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using sudokuvip.Models;
using sudokuvip.Services;

namespace sudokuvip
{
    public partial class MainWindow : Window
    {
        private readonly SudokuEngine _engine = new SudokuEngine();
        private SudokuModel _currentModel = new SudokuModel();
        private readonly Button[,] _cells = new Button[9, 9];
        private UserAccount? _gamePlayer;
        private bool _isGenerating;
        private int _generationVersion;
        private bool _closed;
        private Action<string,string,bool> _showOutcome = (message,title,won) => MessageBox.Show(message,title,
            MessageBoxButton.OK,won ? MessageBoxImage.Information : MessageBoxImage.Warning);
        private bool CanPlay => !_closed && !_isGenerating && _currentModel.State == GameState.Playing;

        private int _selectedRow = -1;
        private int _selectedCol = -1;
        private bool _isPencilMode = false;
        private int _currentDifficulty = 0;

        // Quản lý thời gian
        private readonly DispatcherTimer _gameTimer;
        private int _elapsedSeconds = 0;


        public MainWindow()
        {
            InitializeComponent();

            _gameTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _gameTimer.Tick += GameTimer_Tick;

            UpdateUserProfileUI();
            AuthService.CurrentUserChanged += UpdateUserProfileUI;

            InitializeBoardUI();
            StartGame(0);
        }

        private void GameTimer_Tick(object? sender, EventArgs e)
        {
            if (!CanPlay) return;
            _elapsedSeconds++;
            int minutes = _elapsedSeconds / 60;
            int seconds = _elapsedSeconds % 60;
            txtTimer.Text = $"{minutes:D2}:{seconds:D2}";
        }

        private void InitializeBoardUI()
        {
            BoardGrid.Children.Clear();

            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    Button btn = new Button
                    {
                        Name = $"cell_{r}_{c}",
                        FontSize = 24,
                        FontWeight = FontWeights.Bold,
                        Background = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromRgb(180, 190, 205)),
                        BorderThickness = GetBorderThickness(r, c),
                        Tag = new Point(r, c),
                        Cursor = Cursors.Hand
                    };

                    btn.Click += Cell_Click;
                    _cells[r, c] = btn;
                    BoardGrid.Children.Add(btn);
                }
            }
        }

        private Thickness GetBorderThickness(int row, int col)
        {
            double left = (col % 3 == 0 && col != 0) ? 2.5 : 0.5;
            double top = (row % 3 == 0 && row != 0) ? 2.5 : 0.5;
            double right = (col == 8) ? 0 : 0.5;
            double bottom = (row == 8) ? 0 : 0.5;
            return new Thickness(left, top, right, bottom);
        }

        private async void StartGame(int difficulty)
        {
            _currentDifficulty = difficulty;
            int version = ++_generationVersion;
            _isGenerating = true;
            _gameTimer.Stop();
            var player = AuthService.CurrentUser;
            UpdateControlState();
            var model = await System.Threading.Tasks.Task.Run(() => _engine.StartNewGame(difficulty));
            if (_closed || version != _generationVersion) return;
            _currentModel = model;
            _gamePlayer = player;
            _isGenerating = false;
            _selectedRow = -1;
            _selectedCol = -1;

            _elapsedSeconds = 0;
            txtTimer.Text = "00:00";
            BoardGrid.Visibility = Visibility.Visible;
            _isPencilMode = false;
            UpdatePencilButton();
            UpdateControlState();
            btnPause.Content = "⏸";
            _gameTimer.Start();

            btnHint.Content = $"💡 {_currentModel.HintsLeft}";
            UpdateHeaderDifficultyButtons();
            UpdateUIAfterMove();
            DrawBoard();
            SelectFirstEmptyCell();
        }

        private void SelectFirstEmptyCell()
        {
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    if (!_currentModel.IsFixed[r, c] && _currentModel.CurrentBoard[r, c] == 0)
                    {
                        _selectedRow = r;
                        _selectedCol = c;
                        HighlightSelection();
                        return;
                    }
                }
            }

            // Nếu không có ô trống nào giá trị 0, chọn ô không cố định đầu tiên
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    if (!_currentModel.IsFixed[r, c])
                    {
                        _selectedRow = r;
                        _selectedCol = c;
                        HighlightSelection();
                        return;
                    }
                }
            }
        }

        private void UpdateHeaderDifficultyButtons()
        {
            btnDiffEasy.Foreground = _currentDifficulty == 0 ? new SolidColorBrush(Color.FromRgb(74, 144, 226)) : new SolidColorBrush(Color.FromRgb(160, 160, 160));
            btnDiffEasy.FontWeight = _currentDifficulty == 0 ? FontWeights.Bold : FontWeights.Normal;

            btnDiffMedium.Foreground = _currentDifficulty == 1 ? new SolidColorBrush(Color.FromRgb(74, 144, 226)) : new SolidColorBrush(Color.FromRgb(160, 160, 160));
            btnDiffMedium.FontWeight = _currentDifficulty == 1 ? FontWeights.Bold : FontWeights.Normal;

            btnDiffHard.Foreground = _currentDifficulty == 2 ? new SolidColorBrush(Color.FromRgb(74, 144, 226)) : new SolidColorBrush(Color.FromRgb(160, 160, 160));
            btnDiffHard.FontWeight = _currentDifficulty == 2 ? FontWeights.Bold : FontWeights.Normal;

            btnDiffExpert.Foreground = _currentDifficulty == 3 ? new SolidColorBrush(Color.FromRgb(74, 144, 226)) : new SolidColorBrush(Color.FromRgb(160, 160, 160));
            btnDiffExpert.FontWeight = _currentDifficulty == 3 ? FontWeights.Bold : FontWeights.Normal;
        }

        private void Cell_Click(object sender, RoutedEventArgs e)
        {
            if (!CanPlay) return;

            if (sender is Button btn && btn.Tag is Point pt)
            {
                _selectedRow = (int)pt.X;
                _selectedCol = (int)pt.Y;
                HighlightSelection();
            }
        }

        private void HighlightSelection()
        {
            Brush normalBg = Brushes.White;
            Brush relatedBg = new SolidColorBrush(Color.FromRgb(226, 235, 248)); // Xanh nhạt các ô liên quan
            Brush activeBg = new SolidColorBrush(Color.FromRgb(187, 222, 251));  // Xanh ô đang chọn
            Brush sameNumBg = new SolidColorBrush(Color.FromRgb(207, 226, 254)); // Ô có cùng số

            int targetVal = (_selectedRow >= 0 && _selectedCol >= 0) ? _currentModel.CurrentBoard[_selectedRow, _selectedCol] : 0;

            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    if (r == _selectedRow && c == _selectedCol)
                    {
                        _cells[r, c].Background = activeBg;
                    }
                    else if (targetVal > 0 && _currentModel.CurrentBoard[r, c] == targetVal)
                    {
                        _cells[r, c].Background = sameNumBg;
                    }
                    else if (_selectedRow >= 0 && _selectedCol >= 0 &&
                             (r == _selectedRow || c == _selectedCol || (r / 3 == _selectedRow / 3 && c / 3 == _selectedCol / 3)))
                    {
                        _cells[r, c].Background = relatedBg;
                    }
                    else
                    {
                        _cells[r, c].Background = normalBg;
                    }
                }
            }
        }

        private void Number_Click(object sender, RoutedEventArgs e)
        {
            if (!CanPlay) return;
            if (sender is not Button btn || btn.Content == null) return;

            if (_selectedRow == -1 || _selectedCol == -1 || _currentModel.IsFixed[_selectedRow, _selectedCol])
            {
                SelectFirstEmptyCell();
            }

            if (_selectedRow == -1 || _selectedCol == -1) return;

            if (int.TryParse(btn.Content.ToString(), out int val))
            {
                ApplyNumberInput(val);
            }
        }

        private void ApplyNumberInput(int val)
        {
            if (!CanPlay) return;
            if (_selectedRow < 0 || _selectedCol < 0) SelectFirstEmptyCell();
            if (_selectedRow < 0 || _selectedCol < 0 || _currentModel.IsFixed[_selectedRow,_selectedCol]) return;
            if (_isPencilMode) _engine.ToggleNote(_currentModel,_selectedRow,_selectedCol,val);
            else _engine.MakeMove(_currentModel,_selectedRow,_selectedCol,val);
            RefreshMove();
            if (_engine.IsGameWon(_currentModel)) HandleGameWon();
            else if (_currentModel.Mistakes >= 3) HandleGameOver();
        }

        private void RefreshMove()
        {
            UpdateUIAfterMove();
            btnHint.Content = $"💡 {_currentModel.HintsLeft}";
            if (_selectedRow >= 0 && _selectedCol >= 0) RenderCellContent(_selectedRow,_selectedCol);
            HighlightSelection();
        }

        private void BtnErase_Click(object sender, RoutedEventArgs e)
        {
            if (!CanPlay) return;
            _engine.Erase(_currentModel,_selectedRow,_selectedCol);
            RefreshMove();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (!CanPlay) return;
            var move = _engine.Undo(_currentModel);
            if (move == null) return;
            _selectedRow = move.Row; _selectedCol = move.Col;
            RefreshMove();
        }

        private void BtnPencil_Click(object sender, RoutedEventArgs e)
        {
            if (!CanPlay) return;
            _isPencilMode = !_isPencilMode;
            UpdatePencilButton();
        }
        private void UpdatePencilButton()
        {
            btnPencil.Background = _isPencilMode ? new SolidColorBrush(Color.FromRgb(77, 112, 184)) : new SolidColorBrush(Color.FromRgb(34, 34, 34));
            btnPencil.Foreground = _isPencilMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(160, 160, 160));
        }

        private void BtnHint_Click(object sender, RoutedEventArgs e)
        {
            if (!CanPlay || _selectedRow == -1 || _selectedCol == -1) return;

            if (_currentModel.CurrentBoard[_selectedRow, _selectedCol] == _currentModel.SolutionBoard[_selectedRow, _selectedCol])
            {
                MessageBox.Show("Ô này đã có đáp án chính xác!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_currentModel.HintsLeft <= 0)
            {
                MessageBox.Show("Bạn đã hết lượt gợi ý cho ván đấu này!", "Hết gợi ý", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int correctValue = _engine.GetHint(_currentModel, _selectedRow, _selectedCol);
            if (correctValue != -1)
            {
                btnHint.Content = $"💡 {_currentModel.HintsLeft}";

                UpdateUIAfterMove();
                RenderCellContent(_selectedRow, _selectedCol);
                HighlightSelection();

                if (_engine.IsGameWon(_currentModel))
                {
                    HandleGameWon();
                }
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            if (_isGenerating || _currentModel.State is GameState.Won or GameState.Lost) return;
            _engine.TogglePause(_currentModel);
            bool paused = _currentModel.State == GameState.Paused;
            if (paused) _gameTimer.Stop(); else _gameTimer.Start();
            btnPause.Content = paused ? "▶" : "⏸";
            BoardGrid.Visibility = paused ? Visibility.Hidden : Visibility.Visible;
            UpdateControlState();
        }

        private void UpdateControlState()
        {
            BoardGrid.IsEnabled = CanPlay;
            btnHint.IsEnabled = btnPencil.IsEnabled = btnUndo.IsEnabled = btnErase.IsEnabled = CanPlay;
            btnPause.IsEnabled = !_isGenerating && _currentModel.State is GameState.Playing or GameState.Paused;
            btnLogout.IsEnabled = btnSaveGuestScore.IsEnabled = !_isGenerating && _currentModel.SaveState != ResultSaveState.Saving;
            btnNewGame.IsEnabled = btnDiffEasy.IsEnabled = btnDiffMedium.IsEnabled = btnDiffHard.IsEnabled = btnDiffExpert.IsEnabled =
                !_isGenerating && _currentModel.SaveState != ResultSaveState.Saving;
        }

        protected override void OnClosed(EventArgs e)
        {
            _closed = true; ++_generationVersion;
            _gameTimer.Stop(); _gameTimer.Tick -= GameTimer_Tick;
            AuthService.CurrentUserChanged -= UpdateUserProfileUI;
            base.OnClosed(e);
        }

        private void Difficulty_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag == null) return;
            if (int.TryParse(btn.Tag.ToString(), out int diff))
            {
                StartGame(diff);
            }
        }

        private void BtnNewGame_Click(object sender, RoutedEventArgs e)
        {
            StartGame(_currentDifficulty);
        }

        private void DrawBoard()
        {
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    RenderCellContent(r, c);
                }
            }
        }

        private void RenderCellContent(int r, int c)
        {
            int val = _currentModel.CurrentBoard[r, c];
            var notes = _currentModel.Notes[r, c];
            var btn = _cells[r, c];

            if (val > 0)
            {
                // Hiển thị số lớn
                TextBlock tb = new TextBlock
                {
                    Text = val.ToString(),
                    FontSize = 24,
                    FontWeight = _currentModel.IsFixed[r, c] ? FontWeights.Bold : FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (_currentModel.IsFixed[r, c])
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(20, 20, 20));
                else if (val == _currentModel.SolutionBoard[r, c])
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(44, 94, 170)); // Xanh dương
                else
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Đỏ sai

                btn.Content = tb;
            }
            else if (notes.Count > 0)
            {
                // Hiển thị bảng số ghi chú 3x3
                UniformGrid noteGrid = new UniformGrid { Rows = 3, Columns = 3, Margin = new Thickness(2) };
                for (int n = 1; n <= 9; n++)
                {
                    TextBlock noteTb = new TextBlock
                    {
                        Text = notes.Contains(n) ? n.ToString() : "",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    noteGrid.Children.Add(noteTb);
                }
                btn.Content = noteGrid;
            }
            else
            {
                btn.Content = null;
            }
        }

        private void UpdateUIAfterMove()
        {
            txtScore.Text = _currentModel.Score.ToString();
            txtStatusScore.Text = $"🏆 {_currentModel.Score}";
            txtMistakes.Text = $"{_currentModel.Mistakes}/3";
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space) { e.Handled = true; BtnPause_Click(this, new RoutedEventArgs()); return; }
            if (!CanPlay) return;

            // Xử lý phím số 1-9 từ bàn phím chính và numpad
            int num = -1;
            if (e.Key >= Key.D1 && e.Key <= Key.D9)
                num = e.Key - Key.D1 + 1;
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9)
                num = e.Key - Key.NumPad1 + 1;

            if (num != -1)
            {
                e.Handled = true;
                ApplyNumberInput(num);
                return;
            }

            // Xóa ô
            if (e.Key == Key.Back || e.Key == Key.Delete)
            {
                e.Handled = true;
                BtnErase_Click(this, new RoutedEventArgs());
                return;
            }

            // Bật/tắt chế độ ghi chú
            if (e.Key == Key.N || e.Key == Key.P)
            {
                e.Handled = true;
                BtnPencil_Click(this, new RoutedEventArgs());
                return;
            }

            // Undo
            if (e.Key == Key.U || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
            {
                e.Handled = true;
                BtnUndo_Click(this, new RoutedEventArgs());
                return;
            }

            // Hint
            if (e.Key == Key.H)
            {
                e.Handled = true;
                BtnHint_Click(this, new RoutedEventArgs());
                return;
            }

            // Pause
            if (e.Key == Key.Space)
            {
                e.Handled = true;
                BtnPause_Click(this, new RoutedEventArgs());
                return;
            }

            // Di chuyển ô chọn bằng phím mũi tên
            if (_selectedRow != -1 && _selectedCol != -1)
            {
                if (e.Key == Key.Up && _selectedRow > 0) { _selectedRow--; e.Handled = true; }
                else if (e.Key == Key.Down && _selectedRow < 8) { _selectedRow++; e.Handled = true; }
                else if (e.Key == Key.Left && _selectedCol > 0) { _selectedCol--; e.Handled = true; }
                else if (e.Key == Key.Right && _selectedCol < 8) { _selectedCol++; e.Handled = true; }

                if (e.Handled)
                {
                    HighlightSelection();
                    return;
                }
            }
            else
            {
                if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
                {
                    e.Handled = true;
                    _selectedRow = 0;
                    _selectedCol = 0;
                    HighlightSelection();
                }
            }
        }

        #region QUẢN LÝ TÀI KHOẢN & LỊCH SỬ ĐẤU

        private void HandleGameWon() => FinishGame(true);
        private void HandleGameOver() => FinishGame(false);

        private async void FinishGame(bool won)
        {
            var model = _currentModel;
            if (!_engine.TryFinish(model,won)) return;
            _gameTimer.Stop();
            model.SaveState = ResultSaveState.Saving;
            UpdateControlState();
            string difficulty = GetDifficultyName(_currentDifficulty);
            int duration = _elapsedSeconds;
            var player = _gamePlayer;
            bool saved = await AuthService.RecordGameResultAsync(player,model.GameId,difficulty,model.Score,duration,model.Mistakes,won);
            model.SaveState = saved ? ResultSaveState.Saved : ResultSaveState.Failed;
            if (_closed) return;
            UpdateControlState();
            string status = saved
                ? (player?.IsGuest == true ? "Kết quả đã lưu trong phiên Khách này." : "Kết quả đã được lưu vào SQL Server.")
                : "Không lưu được kết quả. Ván đã kết thúc; dữ liệu SQL chưa được xác nhận.";
            _showOutcome($"{(won ? "🎉 BẠN ĐÃ CHIẾN THẮNG!" : "Game Over!")}\n\nĐộ khó: {difficulty}\nĐiểm: {model.Score}\nThời gian: {duration/60:D2}:{duration%60:D2}\nSố lỗi: {model.Mistakes}/3\n\n{status}",
                won ? "Chiến thắng!" : "Thua cuộc",won);
        }

        private string GetDifficultyName(int diff)
        {
            return diff switch
            {
                0 => "Dễ",
                1 => "Trung bình",
                2 => "Khó",
                3 => "Chuyên gia",
                _ => "Dễ"
            };
        }

        private void UpdateUserProfileUI()
        {
            Dispatcher.Invoke(() =>
            {
                var user = AuthService.CurrentUser;
                if (user != null)
                {
                    txtUserAvatar.Text = string.IsNullOrEmpty(user.Avatar) ? "👤" : user.Avatar;
                    txtUserDisplayName.Text = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
                    txtUserHighScore.Text = user.HighScore.ToString();

                    if (user.IsGuest)
                    {
                        btnLogout.Content = "🔑 Đổi tài khoản";
                        btnSaveGuestScore.Visibility = (user.TotalScore > 0 || user.TotalGames > 0) ? Visibility.Visible : Visibility.Collapsed;
                    }
                    else
                    {
                        btnLogout.Content = "🚪 Đăng xuất";
                        btnSaveGuestScore.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    txtUserAvatar.Text = "👤";
                    txtUserDisplayName.Text = "Khách";
                    txtUserHighScore.Text = "0";
                    btnLogout.Content = "🔑 Đăng nhập";
                    btnSaveGuestScore.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void BtnSaveGuestScore_Click(object sender, RoutedEventArgs e)
        {
            var loginWin = new LoginWindow
            {
                Owner = this
            };
            // Tự động chuyển sang tab đăng ký
            loginWin.ShowDialog();
            UpdateUserProfileUI();
            if (!ReferenceEquals(_gamePlayer,AuthService.CurrentUser)) StartGame(_currentDifficulty);
        }

        private void BtnPvp_Click(object sender, RoutedEventArgs e)
        {
            var pvpLobby = new Pvp.Views.PvpLobbyWindow
            {
                Owner = this
            };
            pvpLobby.ShowDialog();
            UpdateUserProfileUI();
        }

        private void BtnHistory_Click(object sender, RoutedEventArgs e)
        {
            var historyWin = new HistoryWindow
            {
                Owner = this
            };
            historyWin.ShowDialog();
        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)
        {
            var user = AuthService.CurrentUser;
            if (user != null && !user.IsGuest)
            {
                var confirm = MessageBox.Show($"Bạn có muốn đăng xuất tài khoản \"{user.DisplayName}\"?",
                    "Đăng xuất", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            AuthService.Logout();

            var loginWin = new LoginWindow
            {
                Owner = this
            };
            loginWin.ShowDialog();

            if (AuthService.CurrentUser == null)
            {
                AuthService.LoginAsGuest();
            }

            UpdateUserProfileUI();
            StartGame(_currentDifficulty);
        }

        #endregion
    }
}
