using System.ComponentModel;

namespace CodeWF.NetWrapper.Models;

public enum TcpResponseStatus
{
    [Description("成功")] Success,
    [Description("失败")] Fail
}