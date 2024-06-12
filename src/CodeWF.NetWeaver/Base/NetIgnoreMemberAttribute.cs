using System;

namespace CodeWF.NetWeaver.Base
{
    /// <summary>
    ///     忽略的字段或属性
    /// </summary>
    /// <param name="size"></param>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NetIgnoreMemberAttribute : Attribute
    {
    }
}