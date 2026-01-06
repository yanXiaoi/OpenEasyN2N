using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenEasyN2N.manager;
using OpenEasyN2N.model;
using OpenEasyN2N.service;
using OpenEasyN2N.util;
using OpenEasyN2N.view;
using Serilog;

namespace OpenEasyN2N;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public static MainWindow Instance;
    // 启动状态
    public bool StartStatus = false;
    // 监听的IP
    public N2NPeer Peer;
    // 是否正在获取监听的IP
    private bool IsGetPeer;
    // 上一次读取的流量
    private long _lastBytesReceived = 0;
    private long _lastBytesSent = 0;
    // 本次启动总流量
    private long _allBytesReceived = 0;
    private long _allBytesSent = 0;

    // 配置
    private N2NConfig _config;

    // 显示在线成员窗口
    private PeersWindow _peersWindow;
    // 显示Ping工具窗口
    private PingWindow _pingWindow;

    public MainWindow() {
        //读取配置
        _config = AppTool.GetFileData("config/config.json", new N2NConfig());
        InitializeComponent();
        //注入配置
        ApplyConfigToUi(_config);
        Instance = this;
        // 启动异步任务
        this.StartTask();
    }

    // 实现拖动
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // 检查是否按下鼠标左键
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            // 调用系统内置的拖动方法
            this.DragMove();
        }
    }

    // 关闭逻辑
    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown(); // 彻底退出程序
    }

    // 连接supernode
    private void BtnLink_Click(object sender, RoutedEventArgs e)
    {
        this.StartButton.IsEnabled = false;
        try
        {
            if (!this.StartStatus)
            {
                this.StartButton.Content = "连接中...";
                //保存配置
                N2NConfig config = GetConfigFromUi();
                AppTool.SaveDataToFile("config/config.json", config);
                N2NClientService.StartN2N(config, () =>
                {
                    this.StartStatus = false;
                    Log.Information("N2N 服务已关闭");
                    AppTool.RunUI(() =>
                    {
                        this.CurrentIpText.Text = "0.0.0.0";
                        this.StartButton.Background = Brushes.Green;
                        this.StartButton.Content = "启动连接";
                    });
                }, (ip) =>
                {
                    AppTool.RunUI(() =>
                    {
                        this.CurrentIpText.Text = ip;
                        this.StatusDot.Fill = AppTool.GetBrushColor("#00FF00");
                        this.StatusText.Text = "已连接";
                    });
                    Log.Information("N2N 已成功连接超级节点");
                });
                this.StartStatus = true;
                this.StartButton.Background = Brushes.OrangeRed;
                this.StartButton.Content = "停止连接";
            }else
            {
                N2NClientService.StopN2N();
                this.StartButton.Background = Brushes.Green;
                this.StartButton.Content = "启动连接";
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show("启动失败 "+exception.Message);
        }finally
        {
            this.StartButton.IsEnabled = true;
        }
    }

    private async void BtnInstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this.StartStatus) {
                MessageBox.Show("请先停止连接再安装虚拟网卡！");
                return;
            }
            // 初始化虚拟网卡
            string tapPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources","toolkit", "tap-windows.exe");
            bool isReady = await TapNetworkService.SetupTapEnvironmentAsync(tapPath);
            if (!isReady) {
                MessageBox.Show("虚拟网卡安装失败，请卸载干净后重新安装");
                return;
            }
            MessageBox.Show("安装虚拟网卡成功");
        }
        catch (Exception exception)
        {
            MessageBox.Show("安装失败：" + exception.Message);
        }
    }
    // 卸载驱动按钮逻辑
    private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this.StartStatus) {
                MessageBox.Show("请先停止连接再卸载虚拟网卡！");
                return;
            }
            var result = await TapNetworkService.UninstallViaExeAsync();
            if (result) {
                MessageBox.Show("卸载虚拟网卡成功");
            } else {
                MessageBox.Show("卸载失败，请检查管理员权限。");
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show("卸载失败：" + exception.Message);
        }
    }

    // 显示在线成员按钮逻辑
    private void BtnShowPeers_Click(object sender, RoutedEventArgs e)
    {
        // 如果窗口已经打开且在显示中，则激活它，不再新建
        if (_peersWindow != null && _peersWindow.IsLoaded)
        {
            _peersWindow.Activate();
            _peersWindow.Focus(); // 确保获得焦点
            return;
        }
        _peersWindow = new PeersWindow();
        _peersWindow.Show();
    }
    // 显示Ping工具按钮逻辑
    private void BtnShowPing_Click(object sender, RoutedEventArgs e)
    {
        // 如果窗口已经打开且在显示中，则激活它，不再新建
        if (_pingWindow != null && _pingWindow.IsLoaded)
        {
            _pingWindow.Activate();
            _pingWindow.Focus(); // 确保获得焦点
            return;
        }
        _pingWindow = new PingWindow();
        _pingWindow.Show();
    }



    private async void StartTask()
    {
        // 主流程异步运行
        try
        {
            // 使用 PeriodicTimer 代替 while(true) + Sleep，对异步更友好
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync())
            {
                if (!this.StartStatus)
                {
                    UpdateDisconnectedUI();
                    continue;
                }
                // 获取并输出网卡速率
                UpdateNetworkSpeed();
                TryFetchPeerAsync();
                if (this.Peer == null) continue;
                await PingAndUpdateUIAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "监控主循环发生异常");
        }
    }

    private void TryFetchPeerAsync()
    {
        if (this.IsGetPeer) return;
        try
        {
            this.IsGetPeer = true;
            if (N2NManagementService.Peers.Count > 0)
            {
                //随机选择一个 Peer
                var peer = N2NManagementService.Peers.OrderBy(x => Guid.NewGuid()).FirstOrDefault();
                if (peer != null)
                    this.Peer = peer;
            }else
            {
                this.Peer = null;
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "获取 Peer 失败");
        }
        finally
        {
            this.IsGetPeer = false;
        }
    }

    private async Task PingAndUpdateUIAsync()
    {
        try
        {
            // 使用异步 Ping 方法，避免阻塞
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(this.Peer.VirtualIp, 1000);

            AppTool.RunUI(() =>
            {
                if (reply.Status == IPStatus.Success)
                {
                    this.StatusText.Text = $"{this.Peer.Type} {reply.RoundtripTime} ms ({this.Peer.Name})";
                    //根据不同延迟显示不同颜色
                    if (reply.RoundtripTime < 100)
                    {
                        this.StatusDot.Fill = Brushes.Green;
                    }
                    else if (reply.RoundtripTime < 300)
                    {
                        this.StatusDot.Fill = Brushes.Yellow;
                    }
                    else
                    {
                        this.StatusDot.Fill = Brushes.OrangeRed;
                    }
                }
                else
                {
                    this.StatusDot.Fill = Brushes.OrangeRed;
                    this.StatusText.Text = $"{this.Peer.Type} 超时 ({this.Peer.Name})";
                }
            });
        }
        catch (Exception e)
        {
            Log.Error("Ping 过程中发生错误 "+this.Peer.VirtualIp);
        }
    }

    private void UpdateDisconnectedUI()
    {
        if (this.StatusText.Text == "未连接") return;
        _lastBytesReceived = 0; // 重置计数器
        _lastBytesSent = 0;
        AppTool.RunUI(() =>
        {
            this.StatusDot.Fill = AppTool.GetBrushColor("#444444");
            this.StatusText.Text = "未连接";
            this.UploadSpeed.Text = "0 B/s";
            this.DownloadSpeed.Text = "0 B/s";
        });
    }

    // 获取网卡速率
    private void UpdateNetworkSpeed()
    {
        // 指定网卡
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Name == TapNetworkService.TargetAdapterName);
        if (nic == null) return;
        var stats = nic.GetIPStatistics();
        // 第一次运行或断开后重连时，初始化初始值防止出现巨大的跳值
        if (_lastBytesReceived == 0 && _lastBytesSent == 0)
        {
            _lastBytesReceived = stats.BytesReceived;
            _lastBytesSent = stats.BytesSent;
            _allBytesReceived = 0;
            _allBytesSent = 0;
            return;
        }
        // 计算差值 (每秒字节数)
        long bytesIn = stats.BytesReceived - _lastBytesReceived;
        long bytesOut = stats.BytesSent - _lastBytesSent;
        _allBytesReceived += bytesIn;
        _allBytesSent += bytesOut;

        // 更新记录值供下一次循环使用
        _lastBytesReceived = stats.BytesReceived;
        _lastBytesSent = stats.BytesSent;
        // 更新 UI
        AppTool.RunUI(() =>
        {
            this.UploadSpeed.Text = AppTool.FormatSpeed(bytesOut);
            this.DownloadSpeed.Text = AppTool.FormatSpeed(bytesIn);
            this.TotalUpload.Text = AppTool.FormatSpeed(_allBytesSent);
            this.TotalDownload.Text = AppTool.FormatSpeed(_allBytesReceived);
        });
    }

    private void AutoIp_Checked(object sender, RoutedEventArgs e)
    {
        // 复选框被选中时的处理
        this.VirtualIp.IsEnabled = false; // 禁用IP输入框
    }

    private void AutoIp_Unchecked(object sender, RoutedEventArgs e)
    {
        // 复选框取消选中时的处理
        this.VirtualIp.IsEnabled = true; // 启用IP输入框
    }

    private void BtnExtraArgs_Click(object sender, RoutedEventArgs e)
    {
        // 打开弹出框
        ExtraArgsPopup.IsOpen = true;
        ExtraArgsTextBox.Focus();
    }

    private void BtnLog_Click(object sender, RoutedEventArgs e)
    {
        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "log.txt");
        // 检查日志文件是否存在
        if (File.Exists(logDir))
        {
            try
            {
                // 使用系统默认的文本编辑器打开日志文件
                Process.Start(new ProcessStartInfo(logDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开日志文件: {ex.Message}");
            }
        }
        else
        {
            MessageBox.Show("日志文件不存在！");
        }
    }


    #region 配置导入导出逻辑
    // 从文件导入
    private void ImportFromFile_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog();
        openFileDialog.Filter = "JSON文件 (*.json)|*.json|所有文件 (*.*)|*.*";
        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                string json = File.ReadAllText(openFileDialog.FileName);
                var dfConfig = new N2NConfig();
                dfConfig.SuperNode = "默认";
                dfConfig = AppTool.GetFileData(openFileDialog.FileName, dfConfig);
                if (dfConfig.SuperNode != "默认")
                {
                    ApplyConfigToUi(dfConfig);
                    MessageBox.Show("配置导入成功！");
                    // 持久化保存配置
                    AppTool.SaveDataToFile("config/config.json", dfConfig);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解析配置文件失败: {ex.Message}");
            }
        }
    }

    // 从剪贴板导入
    private void ImportFromClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = Clipboard.GetText();
            if (string.IsNullOrEmpty(json))
            {
                MessageBox.Show("剪贴板中没有内容！");
                return;
            }
            N2NConfig importedConfig = JsonSerializer.Deserialize<N2NConfig>(json);
            if (importedConfig != null)
            {
                ApplyConfigToUi(importedConfig);
                MessageBox.Show("从剪贴板导入配置成功！");
                // 持久化保存配置
                AppTool.SaveDataToFile("config/config.json", importedConfig);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"剪贴板内容不是有效的配置格式: {ex.Message}");
        }
    }

    // 导出到文件
    private void ExportToFile_Click(object sender, RoutedEventArgs e)
    {
        Microsoft.Win32.SaveFileDialog saveFileDialog = new Microsoft.Win32.SaveFileDialog();
        saveFileDialog.Filter = "JSON文件 (*.json)|*.json";
        saveFileDialog.FileName = "n2n_config_export.json";
        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                N2NConfig currentConfig = GetConfigFromUi();
                AppTool.SaveDataToFile(saveFileDialog.FileName,currentConfig);
                MessageBox.Show("配置导出成功！");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导出文件失败: {ex.Message}");
            }
        }
    }

    // 导出到剪贴板
    private void ExportToClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            N2NConfig currentConfig = GetConfigFromUi();
            string json = JsonSerializer.Serialize(currentConfig);
            Clipboard.SetText(json);
            MessageBox.Show("配置已复制到剪贴板！");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"复制到剪贴板失败: {ex.Message}");
        }
    }

    #endregion

    #region 辅助工具方法 (用于UI与对象转换)

    // 将 UI 上的值读取并返回一个配置对象
    private N2NConfig GetConfigFromUi()
    {
        return new N2NConfig
        {
            SuperNode = this.SuperNode.Text,
            Community = this.Community.Text,
            VirtualIp = this.VirtualIp.Text,
            Password = this.Password.Text,
            IsAutoGet = this.AutoIp.IsChecked == true,
            ExtraArgs = this.ExtraArgsTextBox.Text
        };
    }

    // 将配置对象的值更新到 UI 上
    private void ApplyConfigToUi(N2NConfig config)
    {
        AppTool.RunUI(() =>
        {
            this.SuperNode.Text = config.SuperNode;
            this.Community.Text = config.Community;
            this.VirtualIp.Text = config.VirtualIp;
            this.Password.Text = config.Password;
            this.AutoIp.IsChecked = config.IsAutoGet;
            this.ExtraArgsTextBox.Text = config.ExtraArgs;
            // 手动触发一次输入框状态更新
            this.VirtualIp.IsEnabled = !config.IsAutoGet;
        });
    }

    #endregion
}