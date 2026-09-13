using System.Windows.Forms;

namespace WindowChromeKit.WinForms.Sample;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // 由 csproj 的 ApplicationHighDpiMode=PerMonitorV2 生成：设置 DPI 模式与默认字体
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
