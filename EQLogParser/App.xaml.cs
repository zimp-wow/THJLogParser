using System.Windows;
using System.Threading.Tasks;

namespace EQLogParser
{
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application
  {
    protected override void OnStartup(StartupEventArgs e)
    {
      base.OnStartup(e);
      var splash = new SplashScreen();
      splash.Show();

      Task.Run(() =>
      {
        // Simulate loading work (replace with real loading if needed)
        System.Threading.Thread.Sleep(1200);
      }).ContinueWith(_ =>
      {
        Dispatcher.Invoke(() =>
        {
          var mainWindow = new MainWindow();
          mainWindow.Show();
          splash.Close();
        });
      });
    }

    private void CloseOverlay_MouseClick(object sender, RoutedEventArgs e) => OverlayUtil.ResetOverlay();
  }
}
