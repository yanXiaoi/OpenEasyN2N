using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OpenEasyN2N.manager;

namespace OpenEasyN2N.view;

public partial class PeersWindow : Window
{
    public PeersWindow()
    {
        InitializeComponent();
        PeersGrid.ItemsSource = N2NManagementService.Peers;
        // 初始化数量
        UpdateCount();
        // 订阅集合改变事件，以便更新“在线人数”文字
        N2NManagementService.Peers.CollectionChanged += Peers_CollectionChanged;
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) this.DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => this.Close();

    private Guid _currentOperationId; // 记录当前最新的操作 ID

    private async void PeersGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PeersGrid.SelectedItem is N2NPeer peer)
        {
            string ip = peer.VirtualIp;
            // 生成一个新的操作 ID，标识这是最新的一次点击
            Guid opId = Guid.NewGuid();
            _currentOperationId = opId;
            // 初始化气泡
            PingPopup.IsOpen = false; // 先强制重置
            PopupText.Text = $"正在 Ping {ip}...";
            PopupDot.Fill = new SolidColorBrush(Colors.Gray);
            PingPopup.IsOpen = true;
            try
            {
                using (Ping pingSender = new Ping())
                {
                    PingReply reply = await pingSender.SendPingAsync(ip, 1000);
                    // 检查：如果在 Ping 的过程中用户又双击了，则当前线程不再继续操作 UI
                    if (opId != _currentOperationId) return;

                    if (reply.Status == IPStatus.Success)
                    {
                        PopupDot.Fill = new SolidColorBrush(Color.FromRgb(0, 255, 0));
                        PopupText.Text = $"成功: {reply.RoundtripTime} ms";
                    }
                    else
                    {
                        PopupDot.Fill = new SolidColorBrush(Color.FromRgb(232, 17, 35));
                        PopupText.Text = $"失败: {reply.Status}";
                    }
                }
            }
            catch (Exception ex)
            {
                if (opId != _currentOperationId) return;
                PopupText.Text = "错误: " + ex.Message;
            }

            // 3. 等待 3 秒
            await Task.Delay(3000);

            // 4. 再次检查：只有当此线程依然是“最新”的线程时，才允许执行关闭动作
            if (opId == _currentOperationId)
            {
                PingPopup.IsOpen = false;
            }
        }
    }

    // 当集合发生 增加、删除、清空 时触发
    private void Peers_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        // UI 更新必须在主线程执行
        Dispatcher.Invoke(() => {
            UpdateCount();
        });
    }

    private void UpdateCount()
    {
        TxtCount.Text = N2NManagementService.Peers.Count.ToString();
    }

// 重要：窗口关闭时取消订阅，防止内存泄漏
    protected override void OnClosed(EventArgs e)
    {
        N2NManagementService.Peers.CollectionChanged -= Peers_CollectionChanged;
        base.OnClosed(e);
    }
}