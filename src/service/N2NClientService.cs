using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using OpenEasyN2N.manager;
using OpenEasyN2N.model;
using Serilog;

namespace OpenEasyN2N.service;

public class N2NClientService
{
    private static Process? _edgeProcess;
    private static readonly Regex IpRegex = new Regex(@"created local tap device IP:\s*(?<ip>(\d{1,3}\.){3}\d{1,3})", RegexOptions.Compiled);
    // 当前IP
    private static string currIp = "";

    /// <summary>
    /// 启动N2N核心进程
    /// </summary>
    public static async void StartN2N(N2NConfig config,Action exitCallback,Action<string> okAction)
    {
        try
        {
            //模拟耗时
            await Task.Delay(10);
            // 构造参数
            List<string> args = new List<string>() {
                "-c", config.Community,
                "-l", config.SuperNode,
                "-d", TapNetworkService.TargetAdapterName
            };
            if(!string.IsNullOrWhiteSpace(config.Password))
                args.AddRange("-k",config.Password);

            if(!config.IsAutoGet)
                args.AddRange("-a",config.VirtualIp);

            //添加额外参数
            if (!string.IsNullOrWhiteSpace(config.ExtraArgs))
            {
                args.AddRange(config.ExtraArgs.Split(' '));
            }

            string edgePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "resources", "n2n", "edge.exe");
            if (!File.Exists(edgePath))
            {
                throw new Exception("启动失败，未找到核心文件 " + edgePath);
            }

            var arguments = string.Join(" ", args);
            Log.Information("启动N2N核心服务，参数：{Args}", arguments);
            var gbk = System.Text.Encoding.GetEncoding("UTF-8");
            // 配置启动信息
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = gbk,
                StandardErrorEncoding = gbk
            };
            _edgeProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            // 读取输出日志
            _edgeProcess.OutputDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Log.Information("[N2N] {Log}", e.Data);

                    // 提取 IP 的逻辑
                    if (e.Data.Contains("created local tap device IP:"))
                    {
                        var match = IpRegex.Match(e.Data);
                        if (match.Success)
                        {
                            currIp = match.Groups["ip"].Value;
                            // 这里可以赋值给你的全局变量或 UI
                            // MainWindow.UiData.LocalIp = localIp;
                        }
                    }

                    if (e.Data.Contains("[OK] edge")) //连接服务器成功
                    {
                        okAction?.Invoke(currIp);
                    }
                }
            };
            _edgeProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Log.Error("[N2N Error] {Log}", e.Data);
            };

            // 进程退出回调
            _edgeProcess.Exited += (sender, args) =>
            {
                exitCallback?.Invoke();
            };

            if (!_edgeProcess.Start()) throw new Exception("无法启动进程");
            _edgeProcess.BeginOutputReadLine();
            _edgeProcess.BeginErrorReadLine();
            // 绑定 JobManager (确保程序退出时杀掉子进程)
            JobManager.AddProcess(_edgeProcess);
        }
        catch (Exception e)
        {
            Log.Error(e, "启动N2N进程失败");
            exitCallback?.Invoke();
        }
    }

    /// <summary>
    /// 停止N2N进程
    /// </summary>
    public static async void StopN2N()
    {
        try
        {
            if (_edgeProcess != null && !_edgeProcess.HasExited)
            {
                _edgeProcess.Kill();
                bool exited = await Task.Run(() => _edgeProcess.WaitForExit(2000));
                if (!exited)
                {
                    Log.Warning("N2N进程未能在2秒内正常退出");
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "停止N2N进程失败");
        }
    }
}