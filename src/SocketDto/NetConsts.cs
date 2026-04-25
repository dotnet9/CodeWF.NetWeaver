namespace SocketDto;

/// <summary>
/// SocketDto 网络通信对象 ID 常量。
/// </summary>
public static class NetConsts
{
    #region 测试对象

    /// <summary>
    /// 测试正确对象 ID。
    /// </summary>
    public const ushort TestCorrectObjectId = 200;

    /// <summary>
    /// 测试不同版本对象 ID。
    /// </summary>
    public const ushort TestDiffVersionObjectId = 201;

    /// <summary>
    /// 测试不同属性对象 ID。
    /// </summary>
    public const ushort TestDiffPropsObjectId = 202;

    #endregion

    #region 请求对象 ID

    /// <summary>
    /// 请求目标类型对象 ID。
    /// </summary>
    public const ushort RequestTargetTypeObjectId = 1;

    /// <summary>
    /// 请求 UDP 组播地址对象 ID。
    /// </summary>
    public const ushort RequestUdpAddressObjectId = 3;

    /// <summary>
    /// 请求基础信息对象 ID。
    /// </summary>
    public const ushort RequestServiceInfoObjectId = 5;

    /// <summary>
    /// 请求进程 ID 列表对象 ID。
    /// </summary>
    public const ushort RequestProcessIDListObjectId = 7;

    /// <summary>
    /// 请求进程详情列表对象 ID。
    /// </summary>
    public const ushort RequestProcessListObjectId = 9;

    /// <summary>
    /// 请求结束进程对象 ID。
    /// </summary>
    public const ushort RequestTerminateProcessObjectId = 13;

    #endregion

    #region 响应对象 ID

    /// <summary>
    /// 响应目标类型对象 ID。
    /// </summary>
    public const ushort ResponseTargetTypeObjectId = 2;

    /// <summary>
    /// 响应 UDP 组播地址对象 ID。
    /// </summary>
    public const ushort ResponseUdpAddressObjectId = 4;

    /// <summary>
    /// 响应基础信息对象 ID。
    /// </summary>
    public const ushort ResponseServiceInfoObjectId = 6;

    /// <summary>
    /// 响应进程 ID 列表对象 ID。
    /// </summary>
    public const ushort ResponseProcessIDListObjectId = 8;

    /// <summary>
    /// 响应进程详情列表对象 ID。
    /// </summary>
    public const ushort ResponseProcessListObjectId = 10;

    /// <summary>
    /// 响应结束进程对象 ID。
    /// </summary>
    public const ushort ResponseTerminateProcessObjectId = 14;

    #endregion

    #region 自动推送对象 ID

    /// <summary>
    /// 完整更新进程信息对象 ID。
    /// </summary>
    public const ushort UpdateProcessListObjectId = 11;

    /// <summary>
    /// 进程结构变化通知对象 ID。
    /// </summary>
    public const ushort ChangeProcessListObjectId = 12;

    #endregion

    #region UDP 对象 ID

    /// <summary>
    /// 实时进程更新 UDP 对象 ID。
    /// </summary>
    public const ushort UpdateRealtimeProcessListObjectId = 200;

    /// <summary>
    /// 常规进程更新 UDP 对象 ID。
    /// </summary>
    public const ushort UpdateGeneralProcessListObjectId = 201;

    #endregion
}
