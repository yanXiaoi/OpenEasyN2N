using System.Diagnostics;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using Brushes = System.Windows.Media.Brushes;

namespace OpenEasyN2N.view;

public partial class ToolboxWindow : Window
{
    private static string ServiceName = "WinIPBroadcast";
    private static ServiceController service;

    public ToolboxWindow()
    {
        InitializeComponent();
        this.Loaded += (s, e) =>
        {
            ChekInstall();
            // 刷新显示状态
            RefreshStatus();
        };
    }

    private void ChekInstall()
    {
        try
        {
            try
            {
                service = new ServiceController(ServiceName);
                Log.Information("当前 WinIPBroadcast 服务状态：{status}", service.Status);
            }
            catch (Exception e)
            {
                //未安装，静默安装
                Log.Error("未找到服务WinIPBroadcast，正在安装...");
                RunCommand("install");
                Log.Information("安装WinIPBroadcast完成");
                service = new ServiceController(ServiceName);
            }
        }
        catch (Exception e)
        {
            Log.Error("未找到服务WinIPBroadcast，请手动安装");
        }
    }

    private void RefreshStatus()
    {
        try
        {
            if (service.Status == ServiceControllerStatus.Running)
            {
                StatusText.Text = "服务已启动";
                StatusDot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 255, 0)); // 绿色
                BtnToggle.IsChecked = true;
            }
            else
            {
                StatusText.Text = "服务已停止";
                StatusDot.Fill = Brushes.Gray;
                BtnToggle.IsChecked = false;
            }
        }
        catch(Exception  e)
        {
            Log.Error(e, "服务未就绪");
            StatusText.Text = "服务未就绪";
            StatusDot.Fill = Brushes.Red;
        }
    }

    private async void BtnToggle_Click(object sender, RoutedEventArgs e)
    {
        if (service == null)
        {
            Log.Warning("服务实例未初始化");
            return;
        }

        bool? isChecked = BtnToggle.IsChecked;
        StatusText.Text = isChecked == true ? "正在启动..." : "正在停止...";
        BtnToggle.IsEnabled = false; // 操作期间禁用开关，防止重复点击

        await Task.Run(() => {
            try
            {
                // 刷新服务状态，确保获取的是最新状态
                service.Refresh();

                if (isChecked == true)
                {
                    if (service.Status != ServiceControllerStatus.Running &&
                        service.Status != ServiceControllerStatus.StartPending)
                    {
                        service.Start();
                        // 等待服务达到“运行中”状态，超时时间设为 10 秒
                        service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }
                else
                {
                    if (service.Status != ServiceControllerStatus.Stopped &&
                        service.Status != ServiceControllerStatus.StopPending)
                    {
                        service.Stop();
                        // 等待服务达到“停止”状态
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"操作服务 {ServiceName} 失败");
            }
        });
        BtnToggle.IsEnabled = true;
        RefreshStatus();
    }

    private static void RunCommand(string arg)
    {
        try
        {
            string winIpBroadcastPath =  Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"resources","toolkit","WinIPBroadcast.exe");
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = winIpBroadcastPath,
                Arguments = arg,
                CreateNoWindow = true,
                UseShellExecute = false,
                Verb = "runas" // 需要管理员权限
            };
            var p = Process.Start(psi);
            p?.WaitForExit();
        }
        catch (Exception ex)
        {
            Log.Error(ex,"执行命令失败");
        }
    }
    // 窗口通用交互
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) => DragMove();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
}