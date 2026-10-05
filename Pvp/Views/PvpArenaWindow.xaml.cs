using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using sudokuvip.Pvp.Models;
using sudokuvip.Pvp.Network;

namespace sudokuvip.Pvp.Views
{
    public partial class PvpArenaWindow : Window
    {
        private readonly MsgStartMatchPayload _matchData;
        private readonly PvpManager _manager = PvpManager.Instance;
        private readonly Button[,] _cells = new Button[9, 9];
        private readonly Border[,] _miniCells = new Border[9, 9];

        private readonly int[,] _board = new int[9, 9];
        private readonly int[,] _solution = new int[9, 9];
        private readonly bool[,] _isFixed = new bool[9, 9];
        private readonly HashSet<int>[,] _notes = new HashSet<int>[9, 9];
        private readonly Stack<(int r, int c, int oldVal, HashSet<int> oldNotes, int oldMistakes)> _undoStack = new();

        private int _selectedRow = -1;
        private int _selectedCol = -1;
        private bool _isPencilMode = false;
        private int _myMistakes = 0;
        private int _myFilled = 0;
        private int _myScore = 0;
        private int _oppFilled = 0;
        private int _oppMistakes = 0;
        private int _totalEmpty = 0;

        private readonly DispatcherTimer _timer;
        private int _elapsedSeconds = 0;
        private bool _isFinished = false;

        public PvpArenaWindow(MsgStartMatchPayload matchData)
        {
            InitializeComponent();
            _matchData = matchData;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) =>
            {
                _elapsedSeconds++;
                txtTimer.Text = $"{_elapsedSeconds / 60:D2}:{_elapsedSeconds % 60:D2}";
            };

            InitializeMatch();
            WirePvpEvents();
        }

        private void InitializeMatch()
        {
            var p1 = _manager.LocalPlayer;
            var p2 = _matchData.Opponent;

            // Setup Header Player Info
            txtP1Avatar.Text = p1?.Avatar ?? "👤";
            txtP1Name.Text = p1?.DisplayName ?? "Bạn";
            txtP1Elo.Text = $"{p1?.EloRating ?? 1200} Elo";

            txtP2Avatar.Text = p2.Avatar;
            txtP2Name.Text = p2.DisplayName;
            txtP2Elo.Text = $"{p2.EloRating} Elo";

            txtDifficultyTag.Text = _matchData.Difficulty switch
            {
                0 => " | Độ khó: Dễ",
                1 => " | Độ khó: Trung bình",
                2 => " | Độ khó: Khó",
                3 => " | Độ khó: Chuyên gia",
                _ => " | Độ khó: Trung bình"
            };

            _totalEmpty = _matchData.Board.TotalEmpty;
            txtP1ProgressText.Text = $"0/{_totalEmpty}";
            txtP2ProgressText.Text = $"0/{_totalEmpty}";

            // Unpack Board
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    int idx = r * 9 + c;
                    _solution[r, c] = _matchData.Board.Solution[idx];
                    int clue = _matchData.Board.Clues[idx];
                    _board[r, c] = clue;
                    _isFixed[r, c] = clue > 0;
                    _notes[r, c] = new HashSet<int>();
                }
            }

            InitializeMainBoardUI();
            InitializeMiniBoardUI();
            UpdateTugOfWar();

            _timer.Start();
        }

        private void WirePvpEvents()
        {
            _manager.OnOpponentProgress += HandleOpponentProgress;
            _manager.OnOpponentEmote += HandleOpponentEmote;
            _manager.OnMatchOver += HandleMatchOver;
        }

        private void UnwirePvpEvents()
        {
            _manager.OnOpponentProgress -= HandleOpponentProgress;
            _manager.OnOpponentEmote -= HandleOpponentEmote;
            _manager.OnMatchOver -= HandleMatchOver;
        }

        #region BOARD INITIALIZATION

        private void InitializeMainBoardUI()
        {
            BoardGrid.Children.Clear();
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    var btn = new Button
                    {
                        Name = $"cell_{r}_{c}",
                        FontSize = 22,
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
                    RenderMainCell(r, c);
                }
            }

            SelectFirstEmptyCell();
        }

        private void InitializeMiniBoardUI()
        {
            MiniBoardGrid.Children.Clear();
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    var bdr = new Border
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(34, 34, 34)),
                        BorderThickness = new Thickness(0.5),
                        Background = _isFixed[r, c] ? new SolidColorBrush(Color.FromRgb(55, 65, 81)) : new SolidColorBrush(Color.FromRgb(20, 20, 20))
                    };
                    _miniCells[r, c] = bdr;
                    MiniBoardGrid.Children.Add(bdr);
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

        #endregion

        #region CELL RENDERING & SELECTION

        private void RenderMainCell(int r, int c)
        {
            int val = _board[r, c];
            var btn = _cells[r, c];
            var notes = _notes[r, c];

            if (val > 0)
            {
                var tb = new TextBlock
                {
                    Text = val.ToString(),
                    FontSize = 22,
                    FontWeight = _isFixed[r, c] ? FontWeights.Bold : FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                if (_isFixed[r, c])
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59));
                else if (val == _solution[r, c])
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Blue
                else
                    tb.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red

                btn.Content = tb;
            }
            else if (notes.Count > 0)
            {
                var noteGrid = new UniformGrid { Rows = 3, Columns = 3, Margin = new Thickness(2) };
                for (int n = 1; n <= 9; n++)
                {
                    noteGrid.Children.Add(new TextBlock
                    {
                        Text = notes.Contains(n) ? n.ToString() : "",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    });
                }
                btn.Content = noteGrid;
            }
            else
            {
                btn.Content = null;
            }
        }

        private void SelectFirstEmptyCell()
        {
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    if (!_isFixed[r, c] && _board[r, c] == 0)
                    {
                        _selectedRow = r; _selectedCol = c;
                        HighlightSelection();
                        return;
                    }
        }

        private void Cell_Click(object sender, RoutedEventArgs e)
        {
            if (_isFinished) return;
            if (sender is Button btn && btn.Tag is Point pt)
            {
                _selectedRow = (int)pt.X;
                _selectedCol = (int)pt.Y;
                HighlightSelection();
            }
        }

        private void HighlightSelection()
        {
            var normalBg = Brushes.White;
            var relatedBg = new SolidColorBrush(Color.FromRgb(239, 246, 255));
            var activeBg = new SolidColorBrush(Color.FromRgb(191, 219, 254));
            var sameNumBg = new SolidColorBrush(Color.FromRgb(219, 234, 254));

            int targetVal = (_selectedRow >= 0 && _selectedCol >= 0) ? _board[_selectedRow, _selectedCol] : 0;

            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    if (r == _selectedRow && c == _selectedCol)
                        _cells[r, c].Background = activeBg;
                    else if (targetVal > 0 && _board[r, c] == targetVal)
                        _cells[r, c].Background = sameNumBg;
                    else if (_selectedRow >= 0 && _selectedCol >= 0 &&
                             (r == _selectedRow || c == _selectedCol || (r / 3 == _selectedRow / 3 && c / 3 == _selectedCol / 3)))
                        _cells[r, c].Background = relatedBg;
                    else
                        _cells[r, c].Background = normalBg;
                }
            }
        }

        #endregion

        #region GAME ACTIONS & MOVE APPLICATION

        private void ApplyNumber(int val)
        {
            if (_isFinished || _selectedRow < 0 || _selectedCol < 0 || _isFixed[_selectedRow, _selectedCol]) return;

            int r = _selectedRow, c = _selectedCol;
            int oldVal = _board[r, c];

            if (_isPencilMode)
            {
                if (_board[r, c] != 0) return;
                _undoStack.Push((r, c, oldVal, new HashSet<int>(_notes[r, c]), _myMistakes));
                if (!_notes[r, c].Remove(val)) _notes[r, c].Add(val);
                RenderMainCell(r, c);
                return;
            }

            if (oldVal == val) return; // No-op

            _undoStack.Push((r, c, oldVal, new HashSet<int>(_notes[r, c]), _myMistakes));
            _notes[r, c].Clear();
            _board[r, c] = val;

            bool isCorrect = val == _solution[r, c];
            if (!isCorrect)
            {
                _myMistakes++;
                txtP1Hearts.Text = GetHearts(_myMistakes);
            }
            else
            {
                if (oldVal != _solution[r, c])
                {
                    _myFilled++;
                    _myScore += 10;
                }
            }

            RenderMainCell(r, c);
            HighlightSelection();
            txtP1ProgressText.Text = $"{_myFilled}/{_totalEmpty}";
            UpdateTugOfWar();

            // Send Move Progress to Server
            int cellIdx = r * 9 + c;
            _ = _manager.SendProgress(_matchData.MatchId, cellIdx, isCorrect, _myMistakes, _myFilled, _totalEmpty, _myScore);
        }

        private void Number_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && int.TryParse(btn.Content?.ToString(), out int val))
            {
                ApplyNumber(val);
            }
        }

        private void BtnErase_Click(object sender, RoutedEventArgs e)
        {
            if (_isFinished || _selectedRow < 0 || _selectedCol < 0 || _isFixed[_selectedRow, _selectedCol]) return;
            int r = _selectedRow, c = _selectedCol;
            if (_board[r, c] == 0 && _notes[r, c].Count == 0) return;

            _undoStack.Push((r, c, _board[r, c], new HashSet<int>(_notes[r, c]), _myMistakes));
            if (_board[r, c] == _solution[r, c]) _myFilled = Math.Max(0, _myFilled - 1);

            _board[r, c] = 0;
            _notes[r, c].Clear();
            RenderMainCell(r, c);
            HighlightSelection();
            txtP1ProgressText.Text = $"{_myFilled}/{_totalEmpty}";
            UpdateTugOfWar();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_isFinished || _undoStack.Count == 0) return;
            var (r, c, oldVal, oldNotes, oldMistakes) = _undoStack.Pop();

            if (_board[r, c] == _solution[r, c] && oldVal != _solution[r, c]) _myFilled = Math.Max(0, _myFilled - 1);
            else if (_board[r, c] != _solution[r, c] && oldVal == _solution[r, c]) _myFilled++;

            _board[r, c] = oldVal;
            _notes[r, c] = new HashSet<int>(oldNotes);
            _myMistakes = oldMistakes;
            txtP1Hearts.Text = GetHearts(_myMistakes);

            _selectedRow = r; _selectedCol = c;
            RenderMainCell(r, c);
            HighlightSelection();
            txtP1ProgressText.Text = $"{_myFilled}/{_totalEmpty}";
            UpdateTugOfWar();
        }

        private void BtnPencil_Click(object sender, RoutedEventArgs e)
        {
            _isPencilMode = !_isPencilMode;
            btnPencil.Background = _isPencilMode ? new SolidColorBrush(Color.FromRgb(37, 99, 235)) : new SolidColorBrush(Color.FromRgb(34, 34, 34));
            btnPencil.Foreground = _isPencilMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(209, 213, 219));
        }

        private string GetHearts(int mistakes) => mistakes switch
        {
            0 => "❤️❤️❤️",
            1 => "❤️❤️🖤",
            2 => "❤️🖤🖤",
            _ => "🖤🖤🖤"
        };

        private void UpdateTugOfWar()
        {
            double p1Pct = _totalEmpty > 0 ? Math.Round((double)_myFilled / _totalEmpty * 100, 1) : 0;
            double p2Pct = _totalEmpty > 0 ? Math.Round((double)_oppFilled / _totalEmpty * 100, 1) : 0;

            txtP1Pct.Text = $"{p1Pct}%";
            txtP2Pct.Text = $"{p2Pct}%";

            double weight1 = Math.Max(2, p1Pct);
            double weight2 = Math.Max(2, p2Pct);
            if (weight1 == 2 && weight2 == 2) { weight1 = 50; weight2 = 50; }

            colP1Bar.Width = new GridLength(weight1, GridUnitType.Star);
            colP2Bar.Width = new GridLength(weight2, GridUnitType.Star);
        }

        #endregion

        #region OPPONENT LIVE SYNC & EMOTES

        private void HandleOpponentProgress(MsgProgressPayload prog)
        {
            Dispatcher.Invoke(() =>
            {
                if (_isFinished) return;
                _oppFilled = prog.FilledCorrect;
                _oppMistakes = prog.Mistakes;
                txtOppMistakes.Text = $"{_oppMistakes} / 3";
                txtP2Hearts.Text = GetHearts(_oppMistakes);
                txtOppScore.Text = prog.Score.ToString();
                txtP2ProgressText.Text = $"{_oppFilled}/{_totalEmpty}";

                // Update Mini Board
                int r = prog.CellIndex / 9;
                int c = prog.CellIndex % 9;
                if (r >= 0 && r < 9 && c >= 0 && c < 9)
                {
                    _miniCells[r, c].Background = prog.IsCorrect
                        ? new SolidColorBrush(Color.FromRgb(34, 197, 94))  // Green
                        : new SolidColorBrush(Color.FromRgb(239, 68, 68)); // Red
                }

                UpdateTugOfWar();
            });
        }

        private void Emote_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Content != null)
            {
                string emote = btn.Content.ToString()!;
                _ = _manager.SendEmote(emote);
                PlayEmoteAnimation(txtP1FloatingEmote, transP1Emote, emote);
            }
        }

        private void HandleOpponentEmote(string emote)
        {
            Dispatcher.Invoke(() =>
            {
                PlayEmoteAnimation(txtP2FloatingEmote, transP2Emote, emote);
            });
        }

        private void PlayEmoteAnimation(TextBlock target, TranslateTransform trans, string emote)
        {
            target.Text = emote;
            var animOpacity = new DoubleAnimation(1.0, 0.0, TimeSpan.FromSeconds(1.8));
            var animY = new DoubleAnimation(0.0, -45.0, TimeSpan.FromSeconds(1.8));
            target.BeginAnimation(OpacityProperty, animOpacity);
            trans.BeginAnimation(TranslateTransform.YProperty, animY);
        }

        private void BtnSurrender_Click(object sender, RoutedEventArgs e)
        {
            if (_isFinished) return;
            var confirm = MessageBox.Show("Bạn có chắc chắn muốn đầu hàng? Trận đấu sẽ kết thúc ngay và tính là một trận thua.",
                "Xác nhận đầu hàng", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                _ = _manager.Surrender(_matchData.MatchId);
            }
        }

        private void HandleMatchOver(PvpMatchResult result)
        {
            Dispatcher.Invoke(() =>
            {
                if (_isFinished) return;
                _isFinished = true;
                _timer.Stop();
                BoardGrid.IsEnabled = false;

                var dlg = new PvpResultDialog(result, _manager.LocalPlayer?.Id ?? "")
                {
                    Owner = this
                };
                dlg.ShowDialog();
                Close();
            });
        }

        #endregion

        #region KEYBOARD HANDLING

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isFinished) return;

            int num = -1;
            if (e.Key >= Key.D1 && e.Key <= Key.D9) num = e.Key - Key.D1 + 1;
            else if (e.Key >= Key.NumPad1 && e.Key <= Key.NumPad9) num = e.Key - Key.NumPad1 + 1;

            if (num != -1) { e.Handled = true; ApplyNumber(num); return; }
            if (e.Key is Key.Back or Key.Delete) { e.Handled = true; BtnErase_Click(this, new RoutedEventArgs()); return; }
            if (e.Key is Key.N or Key.P) { e.Handled = true; BtnPencil_Click(this, new RoutedEventArgs()); return; }
            if (e.Key == Key.U || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
            {
                e.Handled = true; BtnUndo_Click(this, new RoutedEventArgs()); return;
            }

            // Arrow navigation
            if (_selectedRow >= 0 && _selectedCol >= 0)
            {
                if (e.Key == Key.Up && _selectedRow > 0) { _selectedRow--; e.Handled = true; }
                else if (e.Key == Key.Down && _selectedRow < 8) { _selectedRow++; e.Handled = true; }
                else if (e.Key == Key.Left && _selectedCol > 0) { _selectedCol--; e.Handled = true; }
                else if (e.Key == Key.Right && _selectedCol < 8) { _selectedCol++; e.Handled = true; }

                if (e.Handled) HighlightSelection();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _timer.Stop();
            UnwirePvpEvents();
            base.OnClosed(e);
        }

        #endregion
    }
}
