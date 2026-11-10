using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RedZoneClub;

public partial class App : Application
{
    public App()
    {
        // أي خطأ غير متوقع يُسجَّل في ملف ويظهر برسالة بدل أن يُغلق البرنامج بصمت
        DispatcherUnhandledException += (_, e) => { LogCrash(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    static bool shown;
    public static void LogCrash(Exception? ex)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RedZoneClub");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "crash.log");
            File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + ex + "\n\n");
            if (!shown)
            {
                shown = true;
                MessageBox.Show("حدث خطأ غير متوقع:\n" + (ex?.Message ?? "") + "\n\nتفاصيل الخطأ محفوظة في:\n" + path, "Red Zone Club", MessageBoxButton.OK, MessageBoxImage.Warning);
                shown = false;
            }
        }
        catch { }
    }
}
