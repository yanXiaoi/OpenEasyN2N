using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using Serilog;

namespace OpenEasyN2N.service;

public class TapNetworkService {
    public static readonly string TargetAdapterName = "n2n_tap"; // 预设的固定网卡名
    /**
     * 配置虚拟网卡：检测、安装、重命名、调优
     * @driverExecutablePath 驱动程序路径
     */
    public static async Task<bool> SetupTapEnvironmentAsync(string driverExecutablePath) {
        // 检查是否已经存在名为 指定名称 的网卡
        if (GetInterfaceByName(TargetAdapterName) != null) {
            Log.Information("已存在名为 {Name} 的网卡，无需安装", TargetAdapterName);
            return true;
        }
        // 检查是否有未命名的 TAP 网卡存在
        var unnamedTap = GetUnnamedTapInterface();
        if (unnamedTap == null) {
            // 完全没有驱动，执行静默安装
            if (!File.Exists(driverExecutablePath))
            {
                Log.Error("未找到网卡驱动文件 "+driverExecutablePath);
                return false;
            }
            Log.Information("正在安装虚拟网卡驱动...");
            bool installed = await RunProcessAsync(driverExecutablePath, "/S"); // 执行静默安装
            if (!installed)
            {
                Log.Error("网卡驱动安装失败");
                return false;
            }
            for (int i = 0; i < 10; i++) {
                await Task.Delay(1000);
                unnamedTap = GetUnnamedTapInterface();
                if (unnamedTap != null) break;
            }
            if (unnamedTap == null) {
                Log.Error("未找到虚拟网卡");
                return false;
            }else {
                Log.Information("虚拟网卡安装成功");
            }
        }
        return ConfigureAdapter(unnamedTap.Name);
    }
    public static async Task<bool> UninstallViaExeAsync()
    {
        // 默认的安装路径
        string uninstallPath = @"C:\Program Files\TAP-Windows\Uninstall.exe";
        if (!File.Exists(uninstallPath))
        {
            // 如果找不到，尝试 32 位路径
            uninstallPath = @"C:\Program Files (x86)\TAP-Windows\Uninstall.exe";
        }
        if (File.Exists(uninstallPath))
        {
            Log.Information("发现系统安装的驱动卸载程序，正在执行...");
            // /S 是静默卸载参数
            var psi = new ProcessStartInfo(uninstallPath, "/S")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
            Log.Information("虚拟网卡卸载成功");
            return true;
        }
        Log.Warning("未找到系统内置卸载程序，请尝试手动卸载。");
        return false;
    }
    private static NetworkInterface? GetInterfaceByName(string name) {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(nic => nic.Name == name);
    }
    private static NetworkInterface? GetUnnamedTapInterface() {
        // 寻找描述里带有 "TAP-Windows" 的网卡
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(nic => nic.Description.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase)
                                 && nic.Name != TargetAdapterName);
    }

    private static bool ConfigureAdapter(string currentName) {
        try {
            // 重命名并优化网卡
            Log.Information("正在将网卡 {OldName} 重命名为 {NewName} 并设置跃点...", currentName, TargetAdapterName);
            // 重命名网卡
            RunProcess("netsh", $"interface set interface name=\"{currentName}\" newname=\"{TargetAdapterName}\"");
            // 设置跃点为 1 (最高优先级，解决联机搜不到房)
            RunProcess("netsh", $"interface ip set interface \"{TargetAdapterName}\" metric=1");
            Log.Information("虚拟网卡配置成功");
            return true;
        }
        catch { return false; }
    }

    private static async Task<bool> RunProcessAsync(string path, string args) {
        var psi = new ProcessStartInfo(path, args) {
            Verb = "runas", // 必须管理员权限
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        var p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
        return true;
    }

    private static void RunProcess(string cmd, string args)
    {
        var psi = new ProcessStartInfo(cmd, args)
        {
            Verb = "runas",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        Process.Start(psi)?.WaitForExit();
    }
}