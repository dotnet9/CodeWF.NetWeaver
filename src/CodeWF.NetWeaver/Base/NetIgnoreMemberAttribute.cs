using System;

namespace CodeWF.NetWeaver.Base;

/// <summary>
/// 网络忽略成员属性，用于标记在网络传输中需要忽略的字段或属性
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class NetIgnoreMemberAttribute : Attribute
{
}