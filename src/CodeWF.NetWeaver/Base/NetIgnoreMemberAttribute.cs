using System;

namespace CodeWF.NetWeaver.Base
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class NetIgnoreMemberAttribute : Attribute
    {
    }
}