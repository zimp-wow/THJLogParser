using System;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Threading;

namespace EQLogParser
{
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App : Application
  {
    internal static SplashScreen Splash;
    protected override void OnStartup(StartupEventArgs e)
    {
      base.OnStartup(e);
      Splash = new SplashScreen();
      Splash.Show();

      var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
      timer.Tick += (s, args) => DoEvents();
      timer.Start();

      DoEvents();

      Splash.SetStatus("Loading settings...", 10);
      ConfigUtil.Init();
      DoEvents();

      Splash.SetStatus("Settings loaded...", 30);
      DoEvents();

      PlayerManager.Instance.Init();
      Splash.SetStatus("Players loaded...", 60);
      DoEvents();

      Splash.SetStatus("Loading data...", 75);
      DoEvents();

      var _ = DataManager.Instance;
      Splash.SetStatus("Data loaded...", 90);
      DoEvents();

      Splash.SetStatus("Finalizing...", 95);
      DoEvents();

      Splash.SetStatus("Starting...", 100);
      DoEvents();

      timer.Stop();

      var mainWindow = new EQLogParser.MainWindow();
      mainWindow.Show();
      Splash.Close();
    }

    private void CloseOverlay_MouseClick(object sender, RoutedEventArgs e) => OverlayUtil.ResetOverlay();

    // Helper method
    private void DoEvents()
    {
      // Process all UI events
      DispatcherFrame frame = new DispatcherFrame();
      Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
          new DispatcherOperationCallback(ExitFrame), frame);
      Dispatcher.PushFrame(frame);
    }

    private object ExitFrame(object frame)
    {
      ((DispatcherFrame)frame).Continue = false;
      return null;
    }
  }
}
