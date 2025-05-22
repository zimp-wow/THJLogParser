using System.Windows;
using System.Threading.Tasks;
using System.Linq;

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

      Task.Run(async () =>
      {
        Application.Current.Dispatcher.Invoke(() => Splash.SetStatus("Loading settings...", 10));
        var configTask = Task.Run(() => ConfigUtil.Init());
        var playerTask = Task.Run(() => PlayerManager.Instance.Init());
        var dataTask = Task.Run(() => { var _ = DataManager.Instance; });

        // Wait for any one to finish to update progress
        var tasks = new[] { configTask, playerTask, dataTask };
        int completed = 0;
        while (completed < 3)
        {
          var finished = await Task.WhenAny(tasks);
          completed++;
          if (finished == configTask)
            Application.Current.Dispatcher.Invoke(() => Splash.SetStatus("Settings loaded...", 30));
          else if (finished == playerTask)
            Application.Current.Dispatcher.Invoke(() => Splash.SetStatus("Players loaded...", 60));
          else if (finished == dataTask)
            Application.Current.Dispatcher.Invoke(() => Splash.SetStatus("Data loaded...", 90));
          tasks = tasks.Where(t => !t.IsCompleted).ToArray();
        }

        Application.Current.Dispatcher.Invoke(() => Splash.SetStatus("Finalizing...", 95));
        await Task.Delay(200); // brief pause for UI
        Application.Current.Dispatcher.Invoke(() => Splash.SetStatus("Starting...", 100));

        Dispatcher.Invoke(() =>
        {
          var mainWindow = new EQLogParser.MainWindow();
          mainWindow.Show();
          Splash.Close();
        });
      });
    }

    private void CloseOverlay_MouseClick(object sender, RoutedEventArgs e) => OverlayUtil.ResetOverlay();
  }
}
