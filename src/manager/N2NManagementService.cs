using System.Net;
using OpenEasyN2N.model;
using OpenEasyN2N.util;
using Serilog;

namespace OpenEasyN2N.manager;

using System.Net.Sockets;
using System.Text;

public class N2NManagementService
{
    private const int DefaultAdminPort = 5644;
    private const string AdminHost = "127.0.0.1";

    public static ObservableCollectionFast<N2NPeer> Peers { get; } = new ObservableCollectionFast<N2NPeer>();
    private static readonly UdpClient _adminClient = new UdpClient();
    private static readonly IPEndPoint _adminEndpoint = new IPEndPoint(IPAddress.Parse(AdminHost), DefaultAdminPort);
    // 开始异步执行
    public static async void Start()
    {
        try
        {
            _adminClient.Client.SendTimeout = 1000;
            _adminClient.Client.ReceiveTimeout = 1000;

            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync())
            {
                var peers = await GetOnlinePeersAsync();
                AppTool.RunUI(() => {
                    Peers.UpdateIncremental(peers, p => p.ToString());
                });
            }
        }
        catch (Exception e)
        {
            Log.Error(e,"获取所有成员异常！");
        }
    }

    public static async Task<List<N2NPeer>> GetOnlinePeersAsync()
    {
        var peers = new List<N2NPeer>();
        try
        {
            byte[] requestData = Encoding.ASCII.GetBytes("peer_list");
            await _adminClient.SendAsync(requestData, requestData.Length, _adminEndpoint);
            StringBuilder fullResponse = new StringBuilder();
            var gbkEncoding = Encoding.UTF8;
            while (true)
            {
                // 每次读取设置 300ms 令牌
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                try
                {
                    var result = await _adminClient.ReceiveAsync(cts.Token);
                    fullResponse.AppendLine(gbkEncoding.GetString(result.Buffer));
                    if (_adminClient.Available == 0) break;
                }
                catch (OperationCanceledException)
                {
                    // 300ms 到期，认为本轮数据接收完毕
                    break;
                }
            }
            peers = ParsePeers(fullResponse.ToString());
        }
        catch (Exception ex)
        {
            Log.Warning("无法获取在线列表: {Msg}", ex.Message);
        }
        return peers;
    }

    private static List<N2NPeer> ParsePeers(string rawData)
    {
        var list = new List<N2NPeer>();
        var lines = rawData.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        // 用于跟踪当前解析行属于哪个板块
        string currentSectionType = "中转";

        foreach (var line in lines)
        {
            string trimmedLine = line.Trim();

            // 识别板块切换
            if (trimmedLine.Contains("SUPERNODE FORWARD")) {
                currentSectionType = "中转";
                continue;
            }
            if (trimmedLine.Contains("PEER TO PEER")) {
                currentSectionType = "直连";
                continue;
            }
            if (trimmedLine.Contains("SUPERNODES")) {
                currentSectionType = "超级节点";
                continue;
            }

            // 基础过滤：必须包含分隔符，且不能是表头或装饰线
            if (!line.Contains("|") || line.Contains("TAP") || line.Contains("===")) continue;

            var parts = line.Split('|').Select(p => p.Trim()).ToArray();

            // 确保是有效数据行 (通常至少有 ID, IP, MAC, EDGE 这几列)
            if (parts.Length < 4) continue;

            string vIp = parts[1];   // TAP/虚拟IP
            string mac = parts[2];   // MAC
            string pAddr = parts[3]; // EDGE/物理地址
            string name = parts.Length > 4 ? parts[4] : "";

            // 3. 核心过滤规则
            if (mac.StartsWith("01:80", StringComparison.OrdinalIgnoreCase)) continue; // 排除广播
            if (pAddr == "0.0.0.0:0") continue; // 排除无效地址

            // 提取信息并赋值 Type
            if (IsValidIPv4(vIp))
            {
                if (!list.Any(p => p.MacAddr == mac))
                {
                    list.Add(new N2NPeer
                    {
                        VirtualIp = vIp,
                        MacAddr = mac,
                        RealAddr = pAddr,
                        Name = name,
                        Type = currentSectionType // 放入你要求的 type 属性中
                    });
                }
            }
        }
        return list;
    }

// 验证是否为有效的 IPv4 地址
    private static bool IsValidIPv4(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        string[] splitValues = ip.Split('.');
        if (splitValues.Length != 4) return false;
        return splitValues.All(r => byte.TryParse(r, out _));
    }
}

public class N2NPeer
{
    public string Name { get; set; }
    public string VirtualIp { get; set; }
    public string MacAddr { get; set; }
    public string RealAddr { get; set; }
    public string Type { get;set; }
    public override string ToString()
    {
        return $"{Name} {VirtualIp} {MacAddr} {RealAddr} {Type}";
    }
}