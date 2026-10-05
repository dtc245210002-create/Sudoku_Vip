using System.Windows;
using sudokuvip.Database;
using sudokuvip.Services;

namespace sudokuvip
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Khởi tạo Database và các bảng trên SQL Server
            var dbResult = DatabaseHelper.EnsureDatabaseAndTablesExist();
            if (!dbResult.Success)
            {
                MessageBox.Show(
                    $"{dbResult.Message}\n\nỨng dụng vẫn có thể tiếp tục với chế độ Khách (Guest), nhưng các tính năng lưu trữ trên SQL Server sẽ tạm thời không khả dụng cho đến khi kết nối được máy chủ.",
                    "Thông báo CSDL SQL Server",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Hiển thị màn hình đăng nhập
            var loginWindow = new LoginWindow();
            loginWindow.ShowDialog();

            if (loginWindow.IsAuthenticated)
            {
                var mainWindow = new MainWindow();
                MainWindow = mainWindow;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWindow.Show();
            }
            else
            {
                // Người dùng đóng cửa sổ đăng nhập mà không chọn Khách hoặc Đăng nhập -> Thoát app
                Shutdown();
            }
        }
    }
}
