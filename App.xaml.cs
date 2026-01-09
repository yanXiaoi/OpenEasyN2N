using System.Diagnostics;
using System.IO;
using System.Windows;
using OpenEasyN2N.manager;
using OpenEasyN2N.service;
using OpenEasyN2N.util;
using Serilog;

using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace OpenEasyN2N;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        //判断是否是管理员身份运行
        if (!AppTool.IsRunAsAdministrator())
        {
            MessageBox.Show("请以管理员身份运行本程序！");
            Application.Current.Shutdown();
            return;
        }
        // 注册编解码环境
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        initLog();
        Log.Information("===== OpenEasyN2N 程序启动 =====");
        init();
        //启动监听
        N2NManagementService.Start();
        Log.Information("程序运行目录: {BaseDir}", AppDomain.CurrentDomain.BaseDirectory);
    }

    private void initLog()
    {
        // 确定日志目录
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        // 每次启动强制清理之前的日志文件夹
        try
        {
            if (Directory.Exists(logDir))
            {
                Directory.Delete(logDir, true); // 递归删除 logs 文件夹
            }
            Directory.CreateDirectory(logDir); // 重新创建空的 logs 文件夹
        }
        catch (Exception ex)
        {
            // 如果文件被占用导致删除失败，至少保证程序能跑
            Debug.WriteLine("清理日志失败: " + ex.Message);
        }
        string logFilePath = Path.Combine(logDir, "log.txt");
        // 配置 Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Infinite, // 不按天滚动，就用这一个文件
                retainedFileCountLimit: 1,                 // 只保留一个文件
                fileSizeLimitBytes: 10 * 1024 * 1024,      // 限制单个文件大小(10MB)
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();
    }

    // 初始化程序
    private async void init() {
        try
        {
            // 初始化虚拟网卡
            string tapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources","toolkit", "tap-windows.exe");
            bool isReady = await TapNetworkService.SetupTapEnvironmentAsync(tapPath);
            if (!isReady) {
                MessageBox.Show("虚拟网卡初始化失败！请检查是否已安装虚拟网卡驱动程序。");
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "虚拟网卡初始化失败");
            MessageBox.Show("虚拟网卡初始化失败");
            Application.Current.Shutdown();
        }
    }
}