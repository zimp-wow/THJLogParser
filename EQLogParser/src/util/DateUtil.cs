using System;
using System.Globalization;

namespace EQLogParser
{
  internal class DateUtil
  {
    private static readonly log4net.ILog LOG = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

    // counting this thing is really slow
    private string LastDateTimeString = null;
    private double LastDateTime;
    private double increment = 0.0;

    internal static double ToDouble(DateTime dateTime) => dateTime.Ticks / TimeSpan.TicksPerSecond;
    internal static DateTime FromDouble(double value) => new((long)value * TimeSpan.TicksPerSecond);
    internal static string GetCurrentDate(string format) => DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
    internal static string FormatSimpleDate(double seconds) => new DateTime().AddSeconds(seconds).ToString("MMM dd HH:mm:ss", CultureInfo.InvariantCulture);
    internal static string FormatSimpleHMS(double seconds) => new DateTime().AddSeconds(seconds).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    internal static double StandardDateToDouble(string source) => ToDouble(ParseStandardDate(source));
    internal static DateTime ParseStandardDate(string source) => CustomDateTimeParser("MMM dd HH:mm:ss yyyy", source, 5);
    
    internal static string FormatSimpleMS(long ticks)
    {
        if (ticks < 0) ticks = 0; // Ensure non-negative ticks.

        // Convert ticks to total seconds and round to the nearest second.
        var totalSeconds = (long)Math.Round((double)ticks / TimeSpan.TicksPerSecond);

        var hours = totalSeconds / 3600; // Find total hours.
        var minutes = totalSeconds % 3600 / 60; // Find remaining minutes.
        var seconds = totalSeconds % 60; // Find remaining seconds.
        return (hours > 0)
            ? $"{hours:D2}:{minutes:D2}:{seconds:D2}"
            : $"{minutes:D2}:{seconds:D2}";
    }

        internal static uint SimpleTimeToSeconds(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return 0;
            }

            uint h = 0, m = 0, s = 0;

            var split = source.Split(':');

            if (split.Length is 0 or > 3)
            {
                return 0;
            }

            if (split.Length == 1)
            {
                s = StatsUtil.ParseUInt(split[0], 0);
            }
            else if (split.Length == 2)
            {
                m = StatsUtil.ParseUInt(split[0], 0);
                s = StatsUtil.ParseUInt(split[1], 0);

                if (s > 59 || m > 59)
                {
                    return 0;
                }
            }
            else if (split.Length == 3)
            {
                h = StatsUtil.ParseUInt(split[0], 0);
                m = StatsUtil.ParseUInt(split[1], 0);
                s = StatsUtil.ParseUInt(split[2], 0);

                if (s > 59 || m > 59 || h > 23)
                {
                    return 0;
                }
            }

            // Convert to total seconds
            return s + (m * 60) + (h * 60 * 60);
        }
        internal static string FormatSimpleMillis(long ticks)
    {
        if (ticks < 0) ticks = 0; // Ensure non-negative ticks.

        // Convert ticks to total milliseconds and round to the nearest millisecond.
        var totalMilliseconds = (long)Math.Round((double)ticks / TimeSpan.TicksPerMillisecond);

        var seconds = totalMilliseconds / 1000 % 60; // Find total seconds, capped at 60.
        var milliseconds = totalMilliseconds % 1000; // Find remaining milliseconds.
        return $"{seconds:D2}.{milliseconds:D3}";
    }
        internal static string FormatGeneralTime(double seconds, bool showSeconds = false)
    {
      TimeSpan diff = TimeSpan.FromSeconds(seconds);
      string result = "";

      if (diff.Days >= 1)
      {
        switch (diff.Days)
        {
          case 1:
            result += diff.Days + " day";
            break;
          default:
            result += diff.Days + " days";
            break;
        }
      }
      else if (diff.Hours >= 1)
      {
        switch (diff.Hours)
        {
          case 1:
            result += diff.Hours + " hour";
            break;
          default:
            result += diff.Hours + " hours";
            break;
        }
      }
      else if (diff.Minutes >= 1)
      {
        switch (diff.Minutes)
        {
          case 1:
            result += diff.Minutes + " minute";
            break;
          default:
            result += diff.Minutes + " minutes";
            break;
        }
      }
      else if (showSeconds && diff.Seconds >= 1)
      {
        switch (diff.Seconds)
        {
          case 1:
            result += diff.Seconds + " second";
            break;
          default:
            result += diff.Seconds + " seconds";
            break;
        }
      }

      return result;
    }

    // This doesn't currently get called so test if ever needed
    internal static double ParseSimpleDate(string timeString)
    {
      double result = 0;
      if (!string.IsNullOrEmpty(timeString))
      {
        var dateTime = CustomDateTimeParser("MMM dd HH:mm:ss", timeString);
        if (dateTime != DateTime.MinValue)
        {
          result = ToDouble(dateTime);
        }
      }

      return result;
    }

    internal bool HasTimeInRange(double now, string line, int lastMins, out double dateTime)
    {
      bool found = false;
      dateTime = double.NaN;
      if (line.Length > 24)
      {
        dateTime = ParseDate(line);
        if (!double.IsNaN(dateTime))
        {
          TimeSpan diff = TimeSpan.FromSeconds(now - dateTime);
          found = (diff.TotalMinutes < lastMins);
        }
      }
      return found;
    }

    internal double ParseLogDate(string line, out string timeString)
    {
      timeString = line.Substring(1, 24);
      return ParseDate(line);
    }

    internal double ParseDate(string timeString) => ParseDateTime(timeString, out double _);

    internal double ParsePreciseDate(string timeString)
    {
      ParseDateTime(timeString, out double precise);
      return precise;
    }

    private double ParseDateTime(string timeString, out double precise)
    {
      double result = double.NaN;

      if (LastDateTimeString == timeString)
      {
        increment += 0.001;
        precise = LastDateTime + increment;
        return LastDateTime;
      }

      increment = 0.0;
      DateTime dateTime = CustomDateTimeParser("MMM dd HH:mm:ss yyyy", timeString, 5);

      if (dateTime == DateTime.MinValue)
      {
        LastDateTime = double.NaN;
      }
      else
      {
        result = LastDateTime = ToDouble(dateTime);
      }

      LastDateTimeString = timeString;
      precise = result;

      if (double.IsNaN(result))
      {
        LOG.Debug("Invalid Date: " + timeString);
      }

      return result;
    }

    private static DateTime CustomDateTimeParser(string dateFormat, string source, int offset = 0)
    {
      int year = 0;
      int month = 0;
      int day = 0;
      int hour = 0;
      int minute = 0;
      int second = 0;
      int letterMonth = 0;

      if (source.Length - offset >= dateFormat.Length)
      {
        for (int i = 0; i < dateFormat.Length; i++)
        {
          char c = source[offset + i];
          switch (dateFormat[i])
          {
            case 'y':
              year = year * 10 + (c - '0');
              break;
            case 'M':
              if (char.IsLetter(c))
              {
                if (++letterMonth == 3)
                {
                  int cur = offset + i;
                  switch (source[cur - 2])
                  {
                    case 'J':
                    case 'j':
                      month = (source[cur - 1] == 'a') ? 1 : (source[cur] == 'n') ? 6 : 7;
                      break;
                    case 'F':
                    case 'f':
                      month = 2;
                      break;
                    case 'M':
                    case 'm':
                      month = (source[cur] == 'r') ? 3 : 5;
                      break;
                    case 'A':
                    case 'a':
                      month = (source[cur - 1] == 'p') ? 4 : 8;
                      break;
                    case 'S':
                    case 's':
                      month = 9;
                      break;
                    case 'O':
                    case 'o':
                      month = 10;
                      break;
                    case 'N':
                    case 'n':
                      month = 11;
                      break;
                    case 'D':
                    case 'd':
                      month = 12;
                      break;
                  }
                }
              }
              else
              {
                month = month * 10 + (c - '0');
              }
              break;
            case 'd':
              if (char.IsDigit(c))
              {
                day = day * 10 + (c - '0');
              }
              break;
            case 'H':
              hour = hour * 10 + (c - '0');
              break;
            case 'm':
              minute = minute * 10 + (c - '0');
              break;
            case 's':
              second = second * 10 + (c - '0');
              break;
          }
        }
      }

      DateTime result = DateTime.MinValue;

      if (year > 0 && month > 0 && month < 13 && day > 0 && day < 32 && hour >= 0 && hour < 24 && minute >= 0 && minute < 60 && second >= 0 && second < 60)
      {
        try
        {
          result = new DateTime(year, month, day, hour, minute, second, DateTimeKind.Local);
        }
        catch (ArgumentException)
        {
          // do nothing
        }
      }

      return result;
    }
  }
}
