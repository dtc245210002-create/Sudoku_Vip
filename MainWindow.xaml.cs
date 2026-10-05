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
    public class SudokuModel
    {
        public int[,] CurrentBoard { get; set; } = new int[9, 9];
        public int[,] SolutionBoard { get; set; } = new int[9, 9];
        public bool[,] IsFixed { get; set; } = new bool[9, 9];
        public HashSet<int>[,] Notes { get; set; } = new HashSet<int>[9, 9];

        public int Mistakes { get; set; } = 0;
        public int Score { get; set; } = 0;
        public int HintsLeft { get; set; } = 3;

        public SudokuModel()
        {
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    Notes[r, c] = new HashSet<int>();
                }
            }
        }
    }

    public class MoveHistoryItem
    {
        public int Row { get; set; }
        public int Col { get; set; }
        public int PreviousValue { get; set; }
        public int NewValue { get; set; }
        public HashSet<int> PreviousNotes { get; set; } = new HashSet<int>();
        public HashSet<int> NewNotes { get; set; } = new HashSet<int>();
        public int ScoreChange { get; set; }
        public bool WasMistake { get; set; }
    }

    public class SudokuEngine
    {
        private static readonly Random _random = new Random();

        public SudokuModel StartNewGame(int difficultyLevel)
        {
            var model = new SudokuModel();
            GenerateRandomBoard(model);

            // Số ô bị xóa tùy theo mức độ
            int cellsToRemove = difficultyLevel switch
            {
                0 => 32, // Dễ
                1 => 42, // Trung bình
                2 => 50, // Khó
                3 => 56, // Chuyên gia
                _ => 36
            };

            RemoveCells(model, cellsToRemove);
            return model;
        }

        public bool MakeMove(SudokuModel model, int row, int col, int value)
        {
            if (model.IsFixed[row, col]) return false;

            if (model.SolutionBoard[row, col] == value)
            {
                model.CurrentBoard[row, col] = value;
                model.Notes[row, col].Clear();
                model.Score += 10;
                return true;
            }
            else
            {
                model.CurrentBoard[row, col] = value; // Hiển thị số sai màu đỏ
                model.Mistakes++;
                return false;
            }
        }

        public int GetHint(SudokuModel model, int row, int col)
        {
            if (model.HintsLeft > 0 && model.CurrentBoard[row, col] != model.SolutionBoard[row, col])
            {
                model.HintsLeft--;
                int correctValue = model.SolutionBoard[row, col];
                model.CurrentBoard[row, col] = correctValue;
                model.Notes[row, col].Clear();
                model.Score += 5;
                return correctValue;
            }
            return -1;
        }

        public bool IsGameWon(SudokuModel model)
        {
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    if (model.CurrentBoard[r, c] != model.SolutionBoard[r, c])
                        return false;
                }
            }
            return true;
        }

        private void GenerateRandomBoard(SudokuModel model)
        {
            // 1. Tạo bàn cờ Sudoku hợp lệ cơ sở
            int[,] baseGrid = new int[9, 9];
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    baseGrid[r, c] = ((r * 3 + r / 3 + c) % 9) + 1;
                }
            }

            // 2. Xáo trộn các số 1-9
            int[] mapping = Enumerable.Range(1, 9).OrderBy(_ => _random.Next()).ToArray();
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    baseGrid[r, c] = mapping[baseGrid[r, c] - 1];
                }
            }

            // 3. Hoán vị các hàng trong cùng một khối 3x3
            for (int block = 0; block < 3; block++)
            {
                for (int i = 0; i < 2; i++)
                {
                    int r1 = block * 3 + _random.Next(3);
                    int r2 = block * 3 + _random.Next(3);
                    if (r1 != r2)
                    {
                        for (int c = 0; c < 9; c++)
                        {
                            (baseGrid[r1, c], baseGrid[r2, c]) = (baseGrid[r2, c], baseGrid[r1, c]);
                        }
                    }
                }
            }

            // 4. Hoán vị các cột trong cùng một khối 3x3
            for (int block = 0; block < 3; block++)
            {
                for (int i = 0; i < 2; i++)
                {
                    int c1 = block * 3 + _random.Next(3);
                    int c2 = block * 3 + _random.Next(3);
                    if (c1 != c2)
                    {
                        for (int r = 0; r < 9; r++)
                        {
                            (baseGrid[r, c1], baseGrid[r, c2]) = (baseGrid[r, c2], baseGrid[r, c1]);
                        }
                    }
                }
            }

            // Gán vào Solution và Current
            for (int r = 0; r < 9; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    model.SolutionBoard[r, c] = baseGrid[r, c];
                    model.CurrentBoard[r, c] = baseGrid[r, c];
                    model.IsFixed[r, c] = true;
                }
            }
        }

        private void RemoveCells(SudokuModel model, int count)
        {
            List<(int r, int c)> positions = new List<(int, int)>();
            for (int r = 0; r < 9; r++)
                for (int c = 0; c < 9; c++)
                    positions.Add((r, c));

            // Xáo trộn vị trí để xóa ngẫu nhiên
            positions = positions.OrderBy(_ => _random.Next()).ToList();

            int removed = 0;
            foreach (var (r, c) in positions)
            {
                if (removed >= count) break;
                model.CurrentBoard[r, c] = 0;
                model.IsFixed[r, c] = false;
                removed++;
            }
        }
    }

    public partial class MainWindow : Window
    {
        private readonly SudokuEngine _engine = new SudokuEngine();
        private SudokuModel _currentModel = new SudokuModel();
        private readonly Button[,] _cells = new Button[9, 9];
        private readonly Stack<MoveHistoryItem> _undoStack = new Stack<MoveHistoryItem>();

        private int _selectedRow = -1;
        private int _selectedCol = -1;
        private bool _isPencilMode = false;
        private int _currentDifficulty = 0;

        // Quản lý thời gian
        private readonly DispatcherTimer _gameTimer;
        private int _elapsedSeconds = 0;
        private bool _isPaused = false;

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

        private void StartGame(int difficulty)
        {
            _currentDifficulty = difficulty;
            _currentModel = _engine.StartNewGame(difficulty);
            _undoStack.Clear();
            _selectedRow = -1;
            _selectedCol = -1;

            _elapsedSeconds = 0;
            txtTimer.Text = "00:00";
            _isPaused = false;
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
            if (_isPaused) return;

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
            if (_isPaused) return;
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
            if (_selectedRow == -1 || _selectedCol == -1)
            {
                SelectFirstEmptyCell();
            }

            if (_selectedRow == -1 || _selectedCol == -1 || _currentModel.IsFixed[_selectedRow, _selectedCol])
                return;

            int prevVal = _currentModel.CurrentBoard[_selectedRow, _selectedCol];
            var prevNotes = new HashSet<int>(_currentModel.Notes[_selectedRow, _selectedCol]);

            if (_isPencilMode)
            {
                // Bật/tắt ghi chú số nháp
                if (_currentModel.Notes[_selectedRow, _selectedCol].Contains(val))
                    _currentModel.Notes[_selectedRow, _selectedCol].Remove(val);
                else
                    _currentModel.Notes[_selectedRow, _selectedCol].Add(val);

                _undoStack.Push(new MoveHistoryItem
                {
                    Row = _selectedRow,
                    Col = _selectedCol,
                    PreviousValue = prevVal,
                    NewValue = prevVal,
                    PreviousNotes = prevNotes,
                    NewNotes = new HashSet<int>(_currentModel.Notes[_selectedRow, _selectedCol]),
                    ScoreChange = 0,
                    WasMistake = false
                });

                RenderCellContent(_selectedRow, _selectedCol);
                return;
            }

            // Chế độ điền số thông thường
            bool isCorrect = _engine.MakeMove(_currentModel, _selectedRow, _selectedCol, val);

            _undoStack.Push(new MoveHistoryItem
            {
                Row = _selectedRow,
                Col = _selectedCol,
                PreviousValue = prevVal,
                NewValue = val,
                PreviousNotes = prevNotes,
                NewNotes = new HashSet<int>(_currentModel.Notes[_selectedRow, _selectedCol]),
                ScoreChange = isCorrect ? 10 : 0,
                WasMistake = !isCorrect
            });

            UpdateUIAfterMove();
            RenderCellContent(_selectedRow, _selectedCol);
            HighlightSelection();

            if (isCorrect)
            {
                if (_engine.IsGameWon(_currentModel))
                {
                    HandleGameWon();
                }
            }
            else
            {
                if (_currentModel.Mistakes >= 3)
                {
                    HandleGameOver();
                }
            }
        }

        private void BtnErase_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaused || _selectedRow == -1 || _selectedCol == -1 || _currentModel.IsFixed[_selectedRow, _selectedCol])
                return;

            int prevVal = _currentModel.CurrentBoard[_selectedRow, _selectedCol];
            var prevNotes = new HashSet<int>(_currentModel.Notes[_selectedRow, _selectedCol]);

            if (prevVal == 0 && prevNotes.Count == 0) return;

            _currentModel.CurrentBoard[_selectedRow, _selectedCol] = 0;
            _currentModel.Notes[_selectedRow, _selectedCol].Clear();

            _undoStack.Push(new MoveHistoryItem
            {
                Row = _selectedRow,
                Col = _selectedCol,
                PreviousValue = prevVal,
                NewValue = 0,
                PreviousNotes = prevNotes,
                NewNotes = new HashSet<int>(),
                ScoreChange = 0,
                WasMistake = false
            });

            RenderCellContent(_selectedRow, _selectedCol);
            HighlightSelection();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaused || _undoStack.Count == 0) return;

            var lastMove = _undoStack.Pop();
            _currentModel.CurrentBoard[lastMove.Row, lastMove.Col] = lastMove.PreviousValue;
            _currentModel.Notes[lastMove.Row, lastMove.Col] = new HashSet<int>(lastMove.PreviousNotes);

            if (lastMove.WasMistake && _currentModel.Mistakes > 0)
                _currentModel.Mistakes--;

            if (lastMove.ScoreChange > 0)
                _currentModel.Score = Math.Max(0, _currentModel.Score - lastMove.ScoreChange);

            _selectedRow = lastMove.Row;
            _selectedCol = lastMove.Col;

            UpdateUIAfterMove();
            RenderCellContent(lastMove.Row, lastMove.Col);
            HighlightSelection();
        }

        private void BtnPencil_Click(object sender, RoutedEventArgs e)
        {
            _isPencilMode = !_isPencilMode;
            btnPencil.Background = _isPencilMode ? new SolidColorBrush(Color.FromRgb(77, 112, 184)) : new SolidColorBrush(Color.FromRgb(34, 34, 34));
            btnPencil.Foreground = _isPencilMode ? Brushes.White : new SolidColorBrush(Color.FromRgb(160, 160, 160));
        }

        private void BtnHint_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaused || _selectedRow == -1 || _selectedCol == -1) return;

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

            int prevVal = _currentModel.CurrentBoard[_selectedRow, _selectedCol];
            var prevNotes = new HashSet<int>(_currentModel.Notes[_selectedRow, _selectedCol]);

            int correctValue = _engine.GetHint(_currentModel, _selectedRow, _selectedCol);
            if (correctValue != -1)
            {
                btnHint.Content = $"💡 {_currentModel.HintsLeft}";

                _undoStack.Push(new MoveHistoryItem
                {
                    Row = _selectedRow,
                    Col = _selectedCol,
                    PreviousValue = prevVal,
                    NewValue = correctValue,
                    PreviousNotes = prevNotes,
                    NewNotes = new HashSet<int>(),
                    ScoreChange = 5,
                    WasMistake = false
                });

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
            _isPaused = !_isPaused;
            if (_isPaused)
            {
                _gameTimer.Stop();
                btnPause.Content = "▶";
                BoardGrid.Visibility = Visibility.Hidden;
            }
            else
            {
                _gameTimer.Start();
                btnPause.Content = "⏸";
                BoardGrid.Visibility = Visibility.Visible;
            }
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
            if (_isPaused) return;

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

        private void HandleGameWon()
        {
            _gameTimer.Stop();
            string diffName = GetDifficultyName(_currentDifficulty);
            int score = _currentModel.Score;
            int duration = _elapsedSeconds;
            int mistakes = _currentModel.Mistakes;

            AuthService.RecordGameResult(diffName, score, duration, mistakes, true);
            UpdateUserProfileUI();

            string saveStatus = (AuthService.CurrentUser != null && !AuthService.CurrentUser.IsGuest)
                ? "\n\n💾 Kết quả ván đấu đã được lưu vào CSDL SQL Server!"
                : "\n\n💡 Bạn đang chơi chế độ Khách. Đăng nhập để lưu thành tích vào CSDL!";

            MessageBox.Show($"🎉 CHÚC MỪNG! BẠN ĐÃ CHIẾN THẮNG!\n\nĐộ khó: {diffName}\nĐiểm số: {score}\nThời gian: {txtTimer.Text}\nSố lỗi: {mistakes}/3{saveStatus}",
                "Chiến thắng!", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void HandleGameOver()
        {
            _gameTimer.Stop();
            string diffName = GetDifficultyName(_currentDifficulty);
            int score = _currentModel.Score;
            int duration = _elapsedSeconds;
            int mistakes = _currentModel.Mistakes;

            AuthService.RecordGameResult(diffName, score, duration, mistakes, false);
            UpdateUserProfileUI();

            MessageBox.Show($"Game Over! Bạn đã mắc {mistakes} lỗi sai.\n\nĐộ khó: {diffName}\nĐiểm ván này: {score}\nThời gian: {txtTimer.Text}\n\nHãy thử lại một ván mới nhé!",
                "Thua cuộc", MessageBoxButton.OK, MessageBoxImage.Warning);

            StartGame(_currentDifficulty);
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
