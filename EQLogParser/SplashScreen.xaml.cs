using System.Windows;

namespace EQLogParser
{
    public partial class SplashScreen : Window
    {
        public SplashScreen()
        {
            InitializeComponent();
        }

        public void SetStatus(string message, double? percent = null)
        {
            Dispatcher.Invoke(() =>
            {
                statusText.Text = message;
            });
        }
    }
}
