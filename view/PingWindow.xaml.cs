using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;

namespace OpenEasyN2N.view
{
    public partial class PingWindow : Window
    {
        private bool _isRunning = false;

        public PingWindow()
        {
            InitializeComponent();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
            this.Close();
        }
        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            string target = TargetInput.Text.Trim();
            if (string.IsNullOrEmpty(target)) return;

            _isRunning = true;
            BtnStart.IsEnabled = false;
            BtnStop.IsEnabled = true;
            ResultOutput.Clear();
            ResultOutput.AppendText($"--- 正在测试 {target} ---\n");

            while (_isRunning)
            {
                if (RbPing.IsChecked == true)
                    await DoPing(target);
                else if (RbTcping.IsChecked == true)
                    await DoTcping(target);
                else
                    await DoUdping(target); // 新增 UDP 模式

                await Task.Delay(1000);
            }
        }

        private async Task DoUdping(string hostAndPort)
        {
            string host = hostAndPort;
            int port = 80;
            if (hostAndPort.Contains(":"))
            {
                var parts = hostAndPort.Split(':');
                host = parts[0];
                int.TryParse(parts[1], out port);
            }

            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // UDP 不需要建立连接，我们通过发送一个探测字节并尝试接收来测试
                using var udpClient = new UdpClient();
                udpClient.Client.ReceiveTimeout = 2000; // 2秒超时

                // 构造一个简单的探测包（空字节或简单字符串）
                byte[] sendBytes = new byte[0];

                // 解析 IP
                var remoteEP = new IPEndPoint(Dns.GetHostAddresses(host)[0], port);

                // 发送探测包
                await udpClient.SendAsync(sendBytes, sendBytes.Length, remoteEP);

                // 尝试接收回包（UDP 很大程度依赖回包，若无应用监听通常会超时或报错）
                var receiveTask = udpClient.ReceiveAsync();
                var delayTask = Task.Delay(2000);

                var completedTask = await Task.WhenAny(receiveTask, delayTask);
                sw.Stop();

                if (completedTask == receiveTask)
                {
                    // 收到回包，确定端口开放
                    double delay = sw.Elapsed.TotalMilliseconds;
                    AppendResult($"[{timeStr}] UDP {host}:{port} - {delay:F2}ms");
                }
                else
                {
                    // UDP 常见情况：端口开放但没有应用层回包，或者被防火墙拦截
                    // 在网络工具中，通常视作 "Open|Filtered" (开放或被过滤)
                    AppendResult($"[{timeStr}] UDP {host}:{port} - 无响应 ");
                }
            }
            catch (SocketException ex)
            {
                sw.Stop();
                // 如果收到 ICMP Port Unreachable，Socket 会抛出 ConnectionReset 错误
                if (ex.SocketErrorCode == SocketError.ConnectionReset || ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    AppendResult($"[{timeStr}] UDP {host}:{port} - 端口关闭");
                }
                else
                {
                    AppendResult($"[{timeStr}] UDP {host}:{port} - 错误: {ex.SocketErrorCode}");
                }
            }
            catch (Exception ex)
            {
                AppendResult($"[{timeStr}] UDP {host}:{port} - 异常: {ex.Message}");
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _isRunning = false;
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            ResultOutput.AppendText("测试已停止。\n");
        }

        private async Task DoPing(string host)
        {
            try
            {
                using var pinger = new Ping();
                var reply = await pinger.SendPingAsync(host, 1000);
                string timeStr = DateTime.Now.ToString("HH:mm:ss");

                if (reply.Status == IPStatus.Success)
                    AppendResult($"[{timeStr}] 来自 {reply.Address}: 字节=32 时间={reply.RoundtripTime}ms");
                else
                    AppendResult($"[{timeStr}] 请求超时 ({reply.Status})");
            }
            catch (Exception ex) { AppendResult($"错误: {ex.Message}"); _isRunning = false; }
        }

        private async Task DoTcping(string hostAndPort)
        {
            // 1. 解析地址
            string host = hostAndPort;
            int port = 80;
            if (hostAndPort.Contains(":"))
            {
                var parts = hostAndPort.Split(':');
                host = parts[0];
                int.TryParse(parts[1], out port);
            }

            string timeStr = DateTime.Now.ToString("HH:mm:ss");

            try
            {
                // 先获取 Ping 延迟作为基准值
                long pingTime = 3;
                try
                {
                    using var pinger = new Ping();
                    var reply = await pinger.SendPingAsync(host, 1000);
                    if (reply.Status == IPStatus.Success)
                    {
                        pingTime = reply.RoundtripTime;
                    }
                }
                catch { /* Ping 失败不影响 TCP 继续，但基准设为 3 */ }
                //开始 TCP 探测
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using var client = new TcpClient();

                var connectTask = client.ConnectAsync(host, port);
                var delayTask = Task.Delay(2000); // 2秒超时

                var completedTask = await Task.WhenAny(connectTask, delayTask);
                sw.Stop();

                if (completedTask == connectTask && client.Connected)
                {
                    double tcpDelay = sw.Elapsed.TotalMilliseconds;

                    // 4. 关键对比逻辑：
                    // 如果 Ping 成功了，且 TCP 延时明显小于 Ping 延时的 60% (且差值大于 5ms，避免小误差波动)
                    // 或者在非本地情况下 TCP 延时极低 (< 2ms)
                    bool isLocal = host.Equals("localhost") || host.StartsWith("127.") || host.Equals("::1");
                    bool looksFake = false;

                    if (!isLocal)
                    {
                        if (pingTime > 0 && tcpDelay < pingTime * 0.6 && (pingTime - tcpDelay) > 5)
                            looksFake = true;
                        else if (tcpDelay < 2.0)
                            looksFake = true;
                    }
                    if (looksFake)
                        AppendResult($"[{timeStr}] TCP {host}:{port} - 连接失败 (超时)");
                    else
                        AppendResult($"[{timeStr}] TCP {host}:{port} - 延时: {tcpDelay:F2}ms");
                }
                else
                    AppendResult($"[{timeStr}] TCP {host}:{port} - 连接失败 (超时)");
            }
            catch (SocketException ex)
            {
                AppendResult($"[{timeStr}] TCP {host}:{port} - 拒绝连接 ({ex.SocketErrorCode})");
            }
            catch (Exception ex)
            {
                AppendResult($"[{timeStr}] TCP {host}:{port} - 错误: {ex.Message}");
            }
        }

        private void AppendResult(string text)
        {
            Dispatcher.Invoke(() => {
                // 添加新内容
                ResultOutput.AppendText(text + Environment.NewLine);

                // 限制显示条数（例如最多保留 20 行）
                int maxLines = 20;
                if (ResultOutput.LineCount > maxLines)
                {
                    // 获取第一行的长度（包括换行符）并移除
                    int lineLength = ResultOutput.GetLineLength(0);
                    ResultOutput.Select(0, lineLength);
                    ResultOutput.SelectedText = ""; // 移除选中的首行
                }
                // 滚动到底部
                ResultOutput.ScrollToEnd();
            });
        }
    }
}