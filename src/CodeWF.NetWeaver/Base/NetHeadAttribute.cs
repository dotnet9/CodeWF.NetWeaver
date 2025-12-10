using System;

namespace CodeWF.NetWeaver.Base;

/// <summary>
/// 网络头属性，用于标记网络传输对象的头部信息
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class NetHeadAttribute : Attribute
{
    /// <summary>
    /// 获取或设置网络对象的 ID
    /// </summary>
    public ushort Id { get; set; }

    /// <summary>
    /// 获取或设置网络对象的版本
    /// </summary>
    public byte Version { get; set; }

    /// <summary>
    /// 初始化 NetHeadAttribute 类的新实例
    /// </summary>
    /// <param name="id">网络对象的 ID</param>
    /// <param name="version">网络对象的版本</param>
    public NetHeadAttribute(ushort id, byte version)
    {
        Id = id;
        Version = version;
    }
}