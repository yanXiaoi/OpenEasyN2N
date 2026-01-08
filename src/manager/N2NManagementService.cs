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
    // 开始异步执行
    public static async void Start()
    {
        try
        {
            using PeriodicTimer timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync())
            {
                var peers = await GetOnlinePeersAsync();
                AppTool.RunUI(() =>
                {
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
            using var client = new UdpClient();
            client.Client.SendTimeout = 1000;
            client.Client.ReceiveTimeout = 1000;
            string command = "peer_list";
            byte[] requestData = Encoding.ASCII.GetBytes(command);
            await client.SendAsync(requestData, requestData.Length, AdminHost, DefaultAdminPort);

            StringBuilder fullResponse = new StringBuilder();
            var gbkEncoding = Encoding.GetEncoding("UTF-8");
            // 循环读取，直到没有更多数据包
            while (true)
            {
                try
                {
                    // 使用 WaitAsync 设置一个短的读取间隙超时（100-300ms 足够了）
                    var task = client.ReceiveAsync();
                    if (await Task.WhenAny(task, Task.Delay(300)) == task)
                    {
                        var result = await task;
                        fullResponse.AppendLine(gbkEncoding.GetString(result.Buffer));
                    }
                    else
                    {
                        // 300ms 没收到新包，认为 edge 已经发完了
                        break;
                    }
                }
                catch { break; }
            }

            string finalResult = fullResponse.ToString();
            peers = ParsePeers(finalResult);
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