using System.IO;
using System.Net.Mime;
using System.Security.Principal;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Serilog;

namespace OpenEasyN2N.util;

public static class AppTool
{
    /**
     * 返回当前是否是管理员身份运行
     */
    public static bool IsRunAsAdministrator()
    {
        // 获取当前 Windows 用户标识
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        // 创建 Windows 角色访问控制对象
        WindowsPrincipal principal = new WindowsPrincipal(identity);

        // 检查当前用户是否属于本地管理员组
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// 将字节数转换为易读的速率字符串
    /// </summary>
    public static string FormatSpeed(double bytes)
    {
        double kbytes = bytes / 1024.0;
        if (kbytes >= 1024)
            return $"{kbytes / 1024.0:F2} MB/s";
        return $"{kbytes:F1} KB/s";
    }

    /**
     * 运行UI线程任务
     */
    public static void RunUI(Action action)
    {
        //判断当前是否是UI线程
        if (Application.Current.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }


    /**
     * 传入16进制颜色值 #000000 ，返回Brush
     */
    public static Brush GetBrushColor(string color)
    {
        if (string.IsNullOrEmpty(color))
            return Brushes.Transparent;

        try
        {
            // 移除 # 符号
            if (color.StartsWith("#"))
                color = color.Substring(1);

            // 解析颜色值
            if (color.Length == 6)
            {
                // 格式: RRGGBB
                var brush = new SolidColorBrush(Color.FromRgb(
                    Convert.ToByte(color.Substring(0, 2), 16),
                    Convert.ToByte(color.Substring(2, 2), 16),
                    Convert.ToByte(color.Substring(4, 2), 16)
                ));
                brush.Freeze(); // 冻结以提高性能
                return brush;
            }
            else if (color.Length == 8)
            {
                // 格式: AARRGGBB
                var brush = new SolidColorBrush(Color.FromArgb(
                    Convert.ToByte(color.Substring(0, 2), 16),
                    Convert.ToByte(color.Substring(2, 2), 16),
                    Convert.ToByte(color.Substring(4, 2), 16),
                    Convert.ToByte(color.Substring(6, 2), 16)
                ));
                brush.Freeze(); // 冻结以提高性能
                return brush;
            }
            else
            {
                // 尝试使用预定义的颜色名称
                var converter = new BrushConverter();
                return (Brush)converter.ConvertFromString(color);
            }
        }
        catch
        {
            // 解析失败时返回透明画刷
            return Brushes.Transparent;
        }
    }


    /**
     * 读取当前目录下的文件数据
     */
    public static  T GetFileData<T>(string path, T defaultValue)
    {
        //拼接目录
        string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        if (File.Exists(filePath))
        {
            try
            {
                // 读取文件内容
                string content = File.ReadAllText(filePath);
                var result = JsonSerializer.Deserialize<T>(content);
                // 如果反序列化结果为 null，返回传入的默认值
                return result ?? defaultValue;
            }
            catch
            {
                // 读取失败时返回默认值
                return defaultValue;
            }
        }
        else
        {
            // 文件不存在时返回默认值
            return defaultValue;
        }
    }

    /**
     * 将数据保存到文件
     */
    public static async void SaveDataToFile(string path, object data)
    {
        try
        {
            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            //创建目录，保证目录存在
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            // 将数据序列化为 JSON
            string json = JsonSerializer.Serialize(data);
            // 将 JSON 保存到文件中
            await File.WriteAllTextAsync(filePath, json);
        }
        catch (Exception e)
        {
            // 保存失败时记录错误信息
            Log.Error(e, "保存数据失败");
        }
    }
}