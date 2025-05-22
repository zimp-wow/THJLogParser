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
        internal static DateTime sTime;
        internal static DateTime eTime;
    protected override void OnStartup(StartupEventArgs e)
    {
      sTime = DateTime.Now;
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

      var _ = PlayerManager.Instance;
      Splash.SetStatus("Players loaded...", 60);
      DoEvents();

      Splash.SetStatus("Loading data...", 75);
      DoEvents();

      var __ = DataManager.Instance;
      Splash.SetStatus("Data loaded...", 90);
      DoEvents();

      Splash.SetStatus("Finalizing...", 95);
      DoEvents();

      Splash.SetStatus("Starting...", 100);
      DoEvents();

      timer.Stop();
      EQLogParser.MainWindow.MWLog.Value.Info($"Splash Loader processing time: {(DateTime.Now - sTime)}");
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
