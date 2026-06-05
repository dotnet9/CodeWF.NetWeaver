using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using CodeWF.NetWeaver.Base;
using DuplicateNameOne = CodeWF.NetWeaver.Tests.CacheCases.One.DuplicateName;
using DuplicateNameTwo = CodeWF.NetWeaver.Tests.CacheCases.Two.DuplicateName;
using BitFieldDuplicateNameOne = CodeWF.NetWeaver.Tests.BitFieldCacheCases.One.DuplicateName;
using BitFieldDuplicateNameTwo = CodeWF.NetWeaver.Tests.BitFieldCacheCases.Two.DuplicateName;
using Xunit;

namespace CodeWF.NetWeaver.Tests
{
    public class SerializeHelperRegressionUnitTest
    {
        [Fact]
        public void Test_SerializeSupportedScalarTypes_Success()
        {
            var obj = new SupportedScalarTypes
            {
                Flag = true,
                Marker = 'W',
                SignedValue = -12,
                UnsignedValue = ulong.MaxValue - 9
            };

            var buffer = SerializeHelper.SerializeObject(obj);
            var newObj = buffer.DeserializeObject<SupportedScalarTypes>();

            Assert.Equal(obj.Flag, newObj.Flag);
            Assert.Equal(obj.Marker, newObj.Marker);
            Assert.Equal(obj.SignedValue, newObj.SignedValue);
            Assert.Equal(obj.UnsignedValue, newObj.UnsignedValue);
        }

        [Fact]
        public void Test_SerializeInterfaceCollections_Success()
        {
            var obj = new InterfaceCollectionHolder
            {
                Values = new List<int> { 1, 3, 5 },
                Pairs = new Dictionary<int, string>
                {
                    [7] = "seven",
                    [9] = "nine"
                }
            };

            var buffer = SerializeHelper.SerializeObject(obj);
            var newObj = buffer.DeserializeObject<InterfaceCollectionHolder>();

            Assert.NotNull(newObj.Values);
            Assert.Equal([1, 3, 5], newObj.Values);
            Assert.NotNull(newObj.Pairs);
            Assert.Equal("seven", newObj.Pairs![7]);
            Assert.Equal("nine", newObj.Pairs[9]);
        }

        [Fact]
        public void Test_SerializeConcreteCollectionImplementingGenericInterface_Success()
        {
            var obj = new ConcreteCollectionHolder
            {
                Values = new CustomIntList { 2, 4, 8 }
            };

            var buffer = SerializeHelper.SerializeObject(obj);
            var newObj = buffer.DeserializeObject<ConcreteCollectionHolder>();

            Assert.NotNull(newObj.Values);
            Assert.Equal([2, 4, 8], newObj.Values);
        }

        [Fact]
        public void Test_BitFieldCacheUsesFullTypeIdentity()
        {
            var first = new BitFieldDuplicateNameOne
            {
                Value = 10
            };
            var second = new BitFieldDuplicateNameTwo
            {
                Value = 0xabc
            };

            var firstBuffer = first.FieldObjectBuffer();
            var secondBuffer = second.FieldObjectBuffer();

            Assert.Equal(first.Value, firstBuffer.ToFieldObject<BitFieldDuplicateNameOne>().Value);
            Assert.Equal(second.Value, secondBuffer.ToFieldObject<BitFieldDuplicateNameTwo>().Value);
        }

        [Fact]
        public async Task Test_ReadPacketAsync_RejectsInvalidPacketLength()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var acceptTask = listener.AcceptSocketAsync();
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
                using var server = await acceptTask;

                await client.SendAsync(BitConverter.GetBytes(SerializeHelper.PacketHeadLen - 1));

                var (success, buffer, headInfo) = await server.ReadPacketAsync();

                Assert.False(success);
                Assert.Null(buffer);
                Assert.Null(headInfo);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public async Task Test_ReadPacketAsync_ReturnsExactPacketBuffer()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                var acceptTask = listener.AcceptSocketAsync();
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
                using var server = await acceptTask;

                var packet = new TestPacket
                {
                    TaskId = 7,
                    Message = "exact"
                }.Serialize(123);
                await client.SendAsync(packet);

                var (success, buffer, headInfo) = await server.ReadPacketAsync();

                Assert.True(success);
                Assert.NotNull(buffer);
                Assert.NotNull(headInfo);
                Assert.Equal(packet.Length, headInfo.BufferLen);
                Assert.Equal(packet.Length, buffer.Length);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void Test_SerializeTypesWithSameName_DoNotConflict()
        {
            var first = new DuplicateNameOne
            {
                Id = 42,
                Name = "alpha"
            };
            var second = new DuplicateNameTwo
            {
                Sequence = ulong.MaxValue - 1,
                Enabled = true
            };

            var firstBuffer = SerializeHelper.SerializeObject(first);
            var secondBuffer = SerializeHelper.SerializeObject(second);

            var firstResult = firstBuffer.DeserializeObject<DuplicateNameOne>();
            var secondResult = secondBuffer.DeserializeObject<DuplicateNameTwo>();

            Assert.Equal(first.Id, firstResult.Id);
            Assert.Equal(first.Name, firstResult.Name);
            Assert.Equal(second.Sequence, secondResult.Sequence);
            Assert.Equal(second.Enabled, secondResult.Enabled);
        }
    }

    public class SupportedScalarTypes
    {
        public bool Flag { get; set; }
        public char Marker { get; set; }
        public sbyte SignedValue { get; set; }
        public ulong UnsignedValue { get; set; }
    }

    public class InterfaceCollectionHolder
    {
        public IList<int>? Values { get; set; }
        public IDictionary<int, string>? Pairs { get; set; }
    }

    public class ConcreteCollectionHolder
    {
        public CustomIntList? Values { get; set; }
    }

    public class CustomIntList : List<int>
    {
    }

    [NetHead(65530, 1)]
    public class TestPacket : INetObject
    {
        public int TaskId { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}

namespace CodeWF.NetWeaver.Tests.CacheCases.One
{
    public class DuplicateName
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}

namespace CodeWF.NetWeaver.Tests.CacheCases.Two
{
    public class DuplicateName
    {
        public ulong Sequence { get; set; }
        public bool Enabled { get; set; }
    }
}

namespace CodeWF.NetWeaver.Tests.BitFieldCacheCases.One
{
    public class DuplicateName
    {
        [NetFieldOffset(0, 4)]
        public byte Value { get; set; }
    }
}

namespace CodeWF.NetWeaver.Tests.BitFieldCacheCases.Two
{
    public class DuplicateName
    {
        [NetFieldOffset(0, 12)]
        public ushort Value { get; set; }
    }
}
