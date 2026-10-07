using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using LanPE.Core;

namespace LanPE.UI;

/// <summary>应用入口：注册平台与渲染后端，启动主窗口。</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppPaths.EnsureCreated();

        Application.DispatcherUnhandledException += e =>
        {
            try
            {
                NativeMessageBox.Show(e.Exception.ToString(), "LanPE - 未处理异常",
                    NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
                e.Handled = true;
            }
            catch { /* 忽略：关闭阶段无法弹窗 */ }
        };

        // Windows 单一目标：固定 Win32 + Direct2D（AOT 友好）
        Win32Platform.Register();
        Direct2DBackend.Register();

        Application.Run(new MainWindow());
    }
}
