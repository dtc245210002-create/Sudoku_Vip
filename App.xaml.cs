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
