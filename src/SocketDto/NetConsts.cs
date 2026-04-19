namespace SocketDto;

/// <summary>
/// SocketDto 网络通信常量定义类
/// </summary>
public class NetConsts
{
    #region 测试相关

    /// <summary>
    /// 测试正确对象 ID
    /// </summary>
    public const ushort TestCorrectObjectId = 200;

    /// <summary>
    /// 测试不同版本对象 ID
    /// </summary>
    public const ushort TestDiffVersionObjectId = 201;

    /// <summary>
    /// 测试不同属性对象 ID
    /// </summary>
    public const ushort TestDiffPropsObjectId = 202;

    #endregion

    #region 请求对象 ID (1-50)

    /// <summary>
    /// 请求目标类型对象 ID
    /// </summary>
    public const ushort RequestTargetTypeObjectId = 1;

    /// <summary>
    /// 请求 UDP 组播地址对象 ID
    /// </summary>
    public const ushort RequestUdpAddressObjectId = 3;

    /// <summary>
    /// 请求基本信息对象 ID
    /// </summary>
    public const ushort RequestServiceInfoObjectId = 5;

    /// <summary>
    /// 请求进程 ID 列表对象 ID
    /// </summary>
    public const ushort RequestProcessIDListObjectId = 7;

    /// <summary>
    /// 请求进程信息对象 ID
    /// </summary>
    public const ushort RequestProcessListObjectId = 9;

    #endregion

    #region 响应对象 ID (51-100)

    /// <summary>
    /// 响应目标终端类型对象 ID
    /// </summary>
    public const ushort ResponseTargetTypeObjectId = 2;

    /// <summary>
    /// 响应 UDP 组播地址对象 ID
    /// </summary>
    public const ushort ResponseUdpAddressObjectId = 4;

    /// <summary>
    /// 响应基本信息对象 ID
    /// </summary>
    public const ushort ResponseServiceInfoObjectId = 6;

    /// <summary>
    /// 响应进程 ID 列表对象 ID
    /// </summary>
    public const ushort ResponseProcessIDListObjectId = 8;

    /// <summary>
    /// 响应进程信息对象 ID
    /// </summary>
    public const ushort ResponseProcessListObjectId = 10;

    #endregion

    #region 自动命令对象 ID (101-150)

    /// <summary>
    /// 更新进程信息对象 ID
    /// </summary>
    public const ushort UpdateProcessListObjectId = 11;

    /// <summary>
    /// 进程结构变化信息对象 ID
    /// </summary>
    public const ushort ChangeProcessListObjectId = 12;

    #endregion

    #region UDP 对象 ID (200-250)

    /// <summary>
    /// 更新实时进程列表 UDP 对象 ID
    /// </summary>
    public const ushort UpdateRealtimeProcessListObjectId = 200;

    /// <summary>
    /// 更新常规进程列表 UDP 对象 ID
    /// </summary>
    public const ushort UpdateGeneralProcessListObjectId = 201;

    #endregion
}