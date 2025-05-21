using System.Windows;
using System.Threading.Tasks;

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
    }

    private void CloseOverlay_MouseClick(object sender, RoutedEventArgs e) => OverlayUtil.ResetOverlay();
  }
}
