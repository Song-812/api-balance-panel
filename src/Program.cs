using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace BalancePanel
{
    internal static class Program
    {
        [STAThread]
        public static int Main()
        {
#if VPN_PANEL
            PanelKind kind = PanelKind.Vpn;
#else
            PanelKind kind = PanelKind.Api;
#endif
            bool created;
            using(Mutex mutex = new Mutex(true,"Local\\BalancePanel-" + kind,out created)) {
                if(!created) { MessageBox.Show("这个面板已经打开，请检查任务栏。","余额面板"); return 0; }
                try {
                    string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"BalancePanel",kind.ToString());
                    Application app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                    PanelWindow window = new PanelWindow(kind,root); app.MainWindow = window;
                    window.Loaded += delegate { window.Start(); };
                    app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
                        e.Handled = true;
                        MessageBox.Show("操作未完成，请检查设置或重新打开程序。为保护隐私，不显示内部请求与账户信息。","余额面板");
                    };
                    app.Run(window); return 0;
                } catch {
                    MessageBox.Show("程序未能启动。请确认 Windows 已安装 .NET Framework 4.8；API 网页登录还需要 Edge WebView2 Runtime。","余额面板");
                    return 1;
                }
            }
        }
    }
}
