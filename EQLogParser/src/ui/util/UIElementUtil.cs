using log4net;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EQLogParser
{
  class UIElementUtil
    {
        private static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod()?.DeclaringType);
        private static readonly string[] CommonFontFamilies =
        [
          "Arial", "Calibri", "Cambria", "Century Gothic", "Georgia", "Helvetica", "Lucida Sans",
      "Open Sans", "Segoe UI", "Roboto", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana"
        ];

        internal static double CalculateTextBoxHeight(FontFamily fontFamily, double fontSize, Thickness padding, Thickness borderThickness)
        {
            // Create the FormattedText object
            var formattedText = new FormattedText(
              "test",
              System.Globalization.CultureInfo.CurrentCulture,
              FlowDirection.LeftToRight,
              new Typeface(fontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
              fontSize,
              Brushes.Black, // The brush doesn't affect size calculation
              VisualTreeHelper.GetDpi(new Window()).PixelsPerDip // This ensures the text size is scaled correctly for the display DPI
            );

            // Calculate the height required for the text
            var textHeight = formattedText.Height;

            // Add padding and border thickness to the height
            var totalHeight = textHeight + padding.Top + padding.Bottom + borderThickness.Top + borderThickness.Bottom;

            return Math.Round(totalHeight);
        }

        internal static double GetDpi()
        {
            // var dpiTransform = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
            //dpi = dpiTransform.PixelsPerInchX; // DPI X value
            return 96.0; // workaround since I think the framework is scaling for us. This was breaking with 4K displays (120 DPI)
        }
        internal static ReadOnlyCollection<string> GetCommonFontFamilyNames()
        {
            var common = (from fontFamily in GetSystemFontFamilies() where CommonFontFamilies.Contains(fontFamily.Source) select fontFamily.Source).ToList();
            return common.OrderBy(name => name).ToList().AsReadOnly();
        }
        internal static ReadOnlyCollection<FontFamily> GetSystemFontFamilies()
        {
            var systemFontFamilies = new List<FontFamily>();
            foreach (var fontFamily in Fonts.SystemFontFamilies)
            {
                try
                {
                    // trigger the exception
                    _ = fontFamily.FamilyNames;

                    // add the font if it didn't throw
                    systemFontFamilies.Add(fontFamily);
                }
                catch (ArgumentException e)
                {
                    // certain fonts cause WPF 4 to throw an exception when the FamilyNames property is accessed; ignore them
                    Log.Debug(e);
                }
            }

            return systemFontFamilies.OrderBy(f => f.Source).ToList().AsReadOnly();
        }

        internal static BitmapImage CreateBitmap(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.EndInit();
                return bitmap;
            }

            return null;
        }

        internal static void ClearMenuEvents(ItemCollection collection, RoutedEventHandler func)
        {
            foreach (var item in collection)
            {
                if (item is MenuItem m)
                {
                    m.Click -= func;
                }
            }
        }
        internal static void CheckHideTitlePanel(Panel titlePanel, Panel optionsPanel)
    {
      var settingsLoc = optionsPanel.PointToScreen(new Point(0, 0));
      var titleLoc = titlePanel.PointToScreen(new Point(0, 0));

      if ((titleLoc.X + titlePanel.ActualWidth) > (settingsLoc.X + 10))
      {
        titlePanel.Visibility = Visibility.Hidden;
      }
      else
      {
        titlePanel.Visibility = Visibility.Visible;
      }
    }

        internal static void SetComboBoxTitle(ComboBox columns, int count, string value, bool hasSelectAll = false)
        {
            if (columns.Items.Count == 0)
            {
                columns.SelectedIndex = -1;
            }
            else
            {
                if (columns.SelectedItem is not ComboBoxItemDetails selected)
                {
                    selected = hasSelectAll ? columns.Items[2] as ComboBoxItemDetails : columns.Items[0] as ComboBoxItemDetails;
                }

                var total = hasSelectAll ? columns.Items.Count - 2 : columns.Items.Count;
                var countString = total == count ? "All" : count.ToString();
                var text = countString + " " + value + ((total == count) ? "" : " Selected");
                if (text[0] == '0')
                {
                    text = "No" + text[1..];
                }

                if (selected != null)
                {
                    selected.SelectedText = text;
                    columns.SelectedIndex = -1;
                    columns.SelectedItem = selected;
                }
            }
        }

        internal static void SetSize(FrameworkElement element, double height, double width)
    {
      if (!double.IsNaN(height) && element.Height != height)
      {
        element.Height = height;
      }

      if (!double.IsNaN(width) && element.Width != width)
      {
        element.Width = width;
      }
    }
  }
}
