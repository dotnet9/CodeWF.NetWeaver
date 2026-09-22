namespace CodeWF.NetWeaver.Base;

/// <summary>
///     Declares the wire order of a serialized property.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class NetFieldOrderAttribute : Attribute
{
    public NetFieldOrderAttribute(int order)
    {
        Order = order;
    }

    public int Order { get; }
}
