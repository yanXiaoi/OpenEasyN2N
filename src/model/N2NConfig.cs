namespace OpenEasyN2N.model;

public class N2NConfig
{
    /**
     * 超级节点
     */
    public string SuperNode { get; set; } = "";
    /**
     * 唯一房间ID
     */
    public string Community { get; set; } = "";
    /**
     * 绑定的虚拟IP
     */
    public string VirtualIp { get; set; } = "";

    /**
     * 加密密钥
     */
    public string Password { get; set; } = "";
    /**
     * 额外启动参数
     */
    public string ExtraArgs { get; set; } = "";
    /**
     * 是否自动获取IP
     */
    public bool IsAutoGet { get; set; } = false;

    public override string ToString()
    {
        return $"SuperNode: {SuperNode}, Community: {Community}, VirtualIp: {VirtualIp}";
    }
}