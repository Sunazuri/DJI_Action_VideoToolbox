using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DJI_Action_VideoToolbox;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatalException(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) ShowFatalException(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ShowFatalException(e.Exception);
            e.SetObserved();
        };
        Application.Run(new MainForm());
    }

    private static void ShowFatalException(Exception ex)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, "DJI_Action_VideoToolbox_v1.0.8_crash.log");
            File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine + ex + Environment.NewLine + Environment.NewLine, new UTF8Encoding(false));
        }
        catch { }
        try
        {
            MessageBox.Show("未処理例外を記録しました。" + Environment.NewLine + Environment.NewLine + ex.GetType().Name + ": " + ex.Message, "DJI_Action_VideoToolbox_v1.0.8", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
