using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text;
using CodeWF.NetWeaver.Base;

namespace CodeWF.NetWeaver;

/// <summary>
/// 序列化辅助类，提供对象的序列化和反序列化功能
/// </summary>
public partial class SerializeHelper
{
    /// <summary>
    /// 缓存对象属性信息的字典，提高反射效率
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ObjectPropertyInfos = new();

    /// <summary>
    /// 支持直接按标量读写的数据类型
    /// </summary>
    private static readonly HashSet<Type> ScalarTypes =
    [
        typeof(bool),
        typeof(byte),
        typeof(char),
        typeof(decimal),
        typeof(double),
        typeof(float),
        typeof(int),
        typeof(long),
        typeof(sbyte),
        typeof(short),
        typeof(string),
        typeof(uint),
        typeof(ulong),
        typeof(ushort)
    ];

    /// <summary>
    /// 默认编码，用于字符串的序列化和反序列化
    /// </summary>
    public static Encoding DefaultEncoding = Encoding.UTF8;

    /// <summary>
    /// 获取指定类型的属性信息列表，使用缓存提高效率
    /// </summary>
    /// <param name="type">要获取属性的类型</param>
    /// <returns>属性信息列表</returns>
    private static PropertyInfo[] GetProperties(Type type)
    {
        if (ObjectPropertyInfos.TryGetValue(type, out var propertyInfos)) return propertyInfos;

        propertyInfos = type.GetProperties();
        ObjectPropertyInfos[type] = propertyInfos;
        return propertyInfos;
    }

    /// <summary>
    /// 判断类型是否可按基础标量直接序列化
    /// </summary>
    private static bool IsScalarType(Type type)
    {
        return type.IsEnum || ScalarTypes.Contains(type);
    }

    /// <summary>
    /// 判断类型是否为支持的集合类型，并返回集合泛型参数
    /// </summary>
    private static bool TryGetCollectionMetadata(Type type, out Type[] genericArguments, out bool isDictionary)
    {
        static bool IsListDefinition(Type definition)
        {
            return definition == typeof(List<>) ||
                   definition == typeof(IList<>) ||
                   definition == typeof(ICollection<>) ||
                   definition == typeof(IEnumerable<>) ||
                   definition == typeof(IReadOnlyList<>) ||
                   definition == typeof(IReadOnlyCollection<>);
        }

        static bool IsDictionaryDefinition(Type definition)
        {
            return definition == typeof(Dictionary<,>) ||
                   definition == typeof(IDictionary<,>) ||
                   definition == typeof(IReadOnlyDictionary<,>);
        }

        isDictionary = false;
        genericArguments = Type.EmptyTypes;

        // IsGenericType 用来判断当前类型是否是泛型类型。
        // 例如 List<int> / Dictionary<string, int> 会返回 true，普通类和数组则返回 false。
        if (!type.IsGenericType)
        {
            return false;
        }

        // GetGenericTypeDefinition() 会把 List<int> 还原成 List<>，
        // 方便我们统一判断“它属于哪一类泛型容器”。
        var genericTypeDefinition = type.GetGenericTypeDefinition();
        if (IsListDefinition(genericTypeDefinition))
        {
            genericArguments = type.GetGenericArguments();
            return true;
        }

        if (IsDictionaryDefinition(genericTypeDefinition))
        {
            genericArguments = type.GetGenericArguments();
            isDictionary = true;
            return true;
        }

        // 有些属性声明的是自定义集合类型，本身未必就是 List<>/Dictionary<>，
        // 但它可能实现了 IList<T> / IDictionary<TKey, TValue>，所以这里继续检查所有接口。
        foreach (var interfaceType in type.GetInterfaces())
        {
            if (!interfaceType.IsGenericType)
            {
                continue;
            }

            var interfaceDefinition = interfaceType.GetGenericTypeDefinition();
            if (IsListDefinition(interfaceDefinition))
            {
                genericArguments = interfaceType.GetGenericArguments();
                return true;
            }

            if (IsDictionaryDefinition(interfaceDefinition))
            {
                genericArguments = interfaceType.GetGenericArguments();
                isDictionary = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 创建集合实例，优先使用目标类型本身，其次回退到 List/Dictionary
    /// </summary>
    private static object CreateCollectionInstance(Type type, Type[] genericArguments, bool isDictionary)
    {
        // 只有“非接口、非抽象类”才能直接 Activator.CreateInstance(type)。
        if (!type.IsInterface && !type.IsAbstract && Activator.CreateInstance(type) is { } concreteInstance)
        {
            return concreteInstance;
        }

        // 如果属性类型是接口或抽象类型，就回退到一个可实例化的默认实现。
        var fallbackType = isDictionary
            ? typeof(Dictionary<,>).MakeGenericType(genericArguments)
            : typeof(List<>).MakeGenericType(genericArguments[0]);

        return Activator.CreateInstance(fallbackType)
               ?? throw new InvalidOperationException($"Cannot create collection instance for {type.FullName}.");
    }

    /// <summary>
    /// 获取网络对象的头部信息
    /// </summary>
    /// <param name="netObjectType">网络对象类型</param>
    /// <returns>网络对象头部属性</returns>
    /// <exception cref="Exception">当类型未标记 NetHeadAttribute 时抛出异常</exception>
    public static NetHeadAttribute GetNetObjectHead(this Type netObjectType)
    {
        var attribute = netObjectType.GetCustomAttribute<NetHeadAttribute>();
        return attribute ?? throw new Exception(
            $"{netObjectType.Name} has not been marked with the attribute {nameof(NetHeadAttribute)}");
    }

    /// <summary>
    /// 获取网络对象的头部信息
    /// </summary>
    /// <returns>网络对象头部属性</returns>
    /// <exception cref="Exception">当类型未标记 NetHeadAttribute 时抛出异常</exception>
    public static NetHeadAttribute GetNetObjectHead<T>()
    {
        var netObjectType = typeof(T);
        var attribute = netObjectType.GetCustomAttribute<NetHeadAttribute>();
        return attribute ?? throw new Exception(
            $"{netObjectType.Name} has not been marked with the attribute {nameof(NetHeadAttribute)}");
    }

    /// <summary>
    /// 从字节数组中读取网络对象头部信息
    /// </summary>
    /// <param name="buffer">字节数组</param>
    /// <param name="readIndex">读取起始索引</param>
    /// <param name="netObjectHeadInfo">输出的网络对象头部信息</param>
    /// <returns>是否成功读取头部信息</returns>
    public static bool ReadHead(this byte[] buffer, ref int readIndex, out NetHeadInfo netObjectHeadInfo)
    {
        if (ReadHead(buffer.AsSpan(readIndex), out netObjectHeadInfo, out var bytesConsumed))
        {
            readIndex += bytesConsumed;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 从Span<byte>中读取网络对象头部信息（高性能版本）
    /// </summary>
    /// <param name="span">字节Span</param>
    /// <param name="netObjectHeadInfo">输出的网络对象头部信息</param>
    /// <param name="bytesConsumed">消耗的字节数</param>
    /// <returns>是否成功读取头部信息</returns>
    public static bool ReadHead(this Span<byte> span, out NetHeadInfo netObjectHeadInfo, out int bytesConsumed)
    {
        netObjectHeadInfo = null!;
        bytesConsumed = 0;
        // 检查缓冲区长度是否足够
        if (span.Length < PacketHeadLen) return false;

        netObjectHeadInfo = new NetHeadInfo();

        // 使用Span<T>的Slice方法来避免不必要的内存拷贝
        // 读取缓冲区长度
        netObjectHeadInfo.BufferLen = BitConverter.ToInt32(span.Slice(0, sizeof(int)));
        // 读取系统ID
        netObjectHeadInfo.SystemId = BitConverter.ToInt64(span.Slice(sizeof(int), sizeof(long)));
        // 读取对象ID
        netObjectHeadInfo.ObjectId = BitConverter.ToUInt16(span.Slice(sizeof(int) + sizeof(long), sizeof(ushort)));
        // 读取对象版本
        netObjectHeadInfo.ObjectVersion = span[sizeof(int) + sizeof(long) + sizeof(ushort)];
        // 读取时间戳
        netObjectHeadInfo.UnixTimeMilliseconds =
            BitConverter.ToInt64(span.Slice(sizeof(int) + sizeof(long) + sizeof(ushort) + sizeof(byte), sizeof(long)));

        bytesConsumed = PacketHeadLen;
        return true;
    }

    /// <summary>
    /// 检查网络对象头部信息是否匹配指定的类型
    /// </summary>
    /// <typeparam name="T">要检查的类型</typeparam>
    /// <param name="netObjectHeadInfo">网络对象头部信息</param>
    /// <returns>是否匹配</returns>
    public static bool IsNetObject<T>(this NetHeadInfo netObjectHeadInfo)
    {
        var netObjectAttribute = GetNetObjectHead(typeof(T));
        return netObjectAttribute.Id == netObjectHeadInfo.ObjectId &&
               netObjectAttribute.Version == netObjectHeadInfo.ObjectVersion;
    }

    /// <summary>
    /// 检查网络对象头部信息是否匹配指定的类型，但版本号不同
    /// </summary>
    /// <typeparam name="T">要检查的类型</typeparam>
    /// <param name="netObjectHeadInfo">网络对象头部信息</param>
    /// <returns>是否匹配</returns>
    public static bool IsNetObjectDiffVersion<T>(this NetHeadInfo netObjectHeadInfo)
    {
        var netObjectAttribute = GetNetObjectHead(typeof(T));
        return netObjectAttribute.Id == netObjectHeadInfo.ObjectId &&
               netObjectAttribute.Version != netObjectHeadInfo.ObjectVersion;
    }
}
