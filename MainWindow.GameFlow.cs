using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace sudokuvip;

public partial class MainWindow
{
    private GameMode _currentMode = GameMode.Normal;
    private int _pendingDifficulty;
    private bool _variantSelected;

    private static Brush BrushFrom(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

    private void ShowModeSelection()
    {
        if (_closed || _currentModel.SaveState == ResultSaveState.Saving) return;
        StopForNavigation();
        ModeSelectionView.Visibility = Visibility.Visible;
        SetupView.Visibility = GameView.Visibility = Visibility.Collapsed;
        txtFooter.Text = "Sudoku VIP · Chơi theo cách của bạn";
        UpdateControlState();
    }

    private void StopForNavigation()
    {
        _gameTimer.Stop();
        ++_generationVersion; // Ignore any puzzle generation that completes after leaving the view.
        _isGenerating = false;
        PauseOverlay.Visibility = Visibility.Collapsed;
        GenerationOverlay.Visibility = Visibility.Collapsed;
    }

    private void ChooseMode_Click(object sender, RoutedEventArgs e) => ShowModeSelection();

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse(button.Tag?.ToString(), out GameMode mode) ||
            _currentModel.SaveState == ResultSaveState.Saving) return;
        if (mode == GameMode.PvP)
        {
            BtnPvp_Click(sender, e);
            return;
        }
        _currentMode = mode;
        _pendingDifficulty = 0;
        _variantSelected = false;
        ShowSetup();
    }

    private void ChangeSetup_Click(object sender, RoutedEventArgs e)
    {
        if (_currentModel.SaveState == ResultSaveState.Saving) return;
        _pendingDifficulty = _currentDifficulty;
        _variantSelected = _currentMode == GameMode.Variant;
        ShowSetup();
    }

    private void ShowSetup()
    {
        StopForNavigation();
        ModeSelectionView.Visibility = GameView.Visibility = Visibility.Collapsed;
        SetupView.Visibility = Visibility.Visible;
        txtSetupMode.Text = _currentMode switch { GameMode.Normal => "Bình thường", GameMode.Variant => "Biến thể", _ => "PvP" };
        VariantOptions.Visibility = _currentMode == GameMode.Variant ? Visibility.Visible : Visibility.Collapsed;
        txtPvpNotice.Visibility = _currentMode == GameMode.PvP ? Visibility.Visible : Visibility.Collapsed;
        txtSetupHint.Visibility = _currentMode == GameMode.Variant && !_variantSelected ? Visibility.Visible : Visibility.Collapsed;
        btnStartGame.Content = _currentMode == GameMode.PvP ? "Tìm đối thủ" : "Bắt đầu chơi  →";
        UpdateVariantSelection();
        UpdateHeaderDifficultyButtons();
        txtFooter.Text = "Sudoku VIP · Chơi theo cách của bạn";
        UpdateControlState();
    }

    private void Variant4x4_Click(object sender, RoutedEventArgs e)
    {
        if (_currentMode != GameMode.Variant || SetupView.Visibility != Visibility.Visible) return;
        _variantSelected = true;
        txtSetupHint.Visibility = Visibility.Collapsed;
        UpdateVariantSelection();
        UpdateControlState();
    }

    private void UpdateVariantSelection()
    {
        btnVariant4x4.Background = BrushFrom(_variantSelected ? "#203047" : "#1D1D20");
        btnVariant4x4.BorderBrush = BrushFrom(_variantSelected ? "#608BCA" : "#3D3D45");
        txtVariantCheck.Text = _variantSelected ? "⦿" : "○";
        txtVariantCheck.Foreground = BrushFrom(_variantSelected ? "#82B6FF" : "#777785");
        System.Windows.Automation.AutomationProperties.SetItemStatus(btnVariant4x4, _variantSelected ? "Đã chọn" : "Chưa chọn");
    }

    private void StartSelectedGame_Click(object sender, RoutedEventArgs e)
    {
        if (SetupView.Visibility != Visibility.Visible || !btnStartGame.IsEnabled) return;
        StartGame(_pendingDifficulty);
    }

    private void UpdateGameLabels(int? size = null)
    {
        bool mini = (size ?? _currentModel.Size) == 4;
        txtGameTitle.Text = mini ? "Sudoku 4×4" : "Sudoku";
        btnChangeSetup.Content = mini ? "Biến thể Sudoku" : "Bình thường";
        txtGameVariant.Text = mini ? "Mini Sudoku" : "Sudoku cổ điển";
        txtDifficultyBadge.Text = GetDifficultyName(_currentDifficulty);
        txtBoardRule.Text = mini ? "Khối 2×2 · Số 1–4" : "Khối 3×3 · Số 1–9";
        txtKeyboardTip.Text = mini ? "Phím 1–4 để điền · N ghi chú · Space tạm dừng" : "Phím 1–9 để điền · N ghi chú · Space tạm dừng";
        txtFooter.Text = mini ? "Biến thể Sudoku · 4×4 · Khối 2×2" : "Sudoku VIP · Bình thường";
    }

    private void Rules_Click(object sender, RoutedEventArgs e)
    {
        int size = _currentModel.Size, box = _currentModel.BoxSize;
        MessageBox.Show($"Điền số 1–{size} vào mỗi ô trống. Trong từng hàng, từng cột và từng khối {box}×{box}, mỗi số chỉ xuất hiện một lần. Ô cho sẵn không thể sửa.",
            "Luật Sudoku", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
