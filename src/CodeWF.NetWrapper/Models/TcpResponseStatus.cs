using System.ComponentModel;

namespace CodeWF.NetWrapper.Models;

/// <summary>
/// TCP 响应状态枚举
/// </summary>
public enum TcpResponseStatus
{
    /// <summary>
    /// 成功
    /// </summary>
    [Description("成功")] Success,
    
    /// <summary>
    /// 失败
    /// </summary>
    [Description("失败")] Fail
}