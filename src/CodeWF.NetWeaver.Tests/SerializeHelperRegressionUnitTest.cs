using DuplicateNameOne = CodeWF.NetWeaver.Tests.CacheCases.One.DuplicateName;
using DuplicateNameTwo = CodeWF.NetWeaver.Tests.CacheCases.Two.DuplicateName;
using BitFieldDuplicateNameOne = CodeWF.NetWeaver.Tests.BitFieldCacheCases.One.DuplicateName;
using BitFieldDuplicateNameTwo = CodeWF.NetWeaver.Tests.BitFieldCacheCases.Two.DuplicateName;

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

            var buffer = obj.SerializeObject();
            var newObj = buffer.DeserializeObject<SupportedScalarTypes>();

            Assert.Equal(obj.Flag, newObj.Flag);
            Assert.Equal(obj.Marker, newObj.Marker);
            Assert.Equal(obj.SignedValue, newObj.SignedValue);
            Assert.Equal(obj.UnsignedValue, newObj.UnsignedValue);
        }

        [Fact]
        public void Test_SerializeExtendedScalarTypes_Success()
        {
            var obj = new ExtendedScalarTypes
            {
                HalfValue = (Half)1.5,
                NativeInt = new IntPtr(1234567890),
                NativeUInt = new UIntPtr(1234567890UL),
                Signed128 = ((Int128)long.MaxValue) + 42,
                Unsigned128 = ((UInt128)ulong.MaxValue) + 42,
                Date = new DateOnly(2026, 6, 24),
                Time = new TimeOnly(10, 39, 23).Add(TimeSpan.FromTicks(4567)),
                Duration = TimeSpan.FromDays(1) + TimeSpan.FromMilliseconds(234),
                Id = Guid.Parse("f8de2f8a-86df-4485-b1ad-7c9f4c88d638")
            };

            var buffer = obj.SerializeObject();
            var newObj = buffer.DeserializeObject<ExtendedScalarTypes>();

            Assert.Equal(obj.HalfValue, newObj.HalfValue);
            Assert.Equal(obj.NativeInt, newObj.NativeInt);
            Assert.Equal(obj.NativeUInt, newObj.NativeUInt);
            Assert.Equal(obj.Signed128, newObj.Signed128);
            Assert.Equal(obj.Unsigned128, newObj.Unsigned128);
            Assert.Equal(obj.Date, newObj.Date);
            Assert.Equal(obj.Time, newObj.Time);
            Assert.Equal(obj.Duration, newObj.Duration);
            Assert.Equal(obj.Id, newObj.Id);
        }

        [Fact]
        public void Test_SerializeNullableScalarTypes_Success()
        {
            var empty = new NullableScalarTypes();
            var emptyBuffer = empty.SerializeObject();
            var emptyResult = emptyBuffer.DeserializeObject<NullableScalarTypes>();

            Assert.Null(emptyResult.RemainingSeconds);
            Assert.Null(emptyResult.Count);
            Assert.Null(emptyResult.SnapshotTime);
            Assert.Null(emptyResult.SendTime);
            Assert.Null(emptyResult.HalfValue);
            Assert.Null(emptyResult.NativeInt);
            Assert.Null(emptyResult.NativeUInt);
            Assert.Null(emptyResult.Signed128);
            Assert.Null(emptyResult.Unsigned128);
            Assert.Null(emptyResult.Date);
            Assert.Null(emptyResult.Time);
            Assert.Null(emptyResult.Duration);
            Assert.Null(emptyResult.Id);

            var obj = new NullableScalarTypes
            {
                RemainingSeconds = 12.5,
                Count = 3,
                SnapshotTime = new DateTime(2026, 6, 24, 10, 39, 23, DateTimeKind.Utc),
                SendTime = new DateTimeOffset(2026, 6, 24, 18, 39, 23, TimeSpan.FromHours(8)),
                HalfValue = (Half)2.25,
                NativeInt = new IntPtr(-3),
                NativeUInt = new UIntPtr(4UL),
                Signed128 = Int128.MinValue + 42,
                Unsigned128 = UInt128.MaxValue - 42,
                Date = new DateOnly(2026, 6, 24),
                Time = new TimeOnly(10, 39, 23),
                Duration = TimeSpan.FromSeconds(45),
                Id = Guid.Parse("53b74d95-8732-4d12-86ff-8f100bf9100c")
            };
            var buffer = obj.SerializeObject();
            var newObj = buffer.DeserializeObject<NullableScalarTypes>();

            Assert.Equal(obj.RemainingSeconds, newObj.RemainingSeconds);
            Assert.Equal(obj.Count, newObj.Count);
            Assert.Equal(obj.SnapshotTime, newObj.SnapshotTime);
            Assert.Equal(obj.SendTime, newObj.SendTime);
            Assert.Equal(obj.HalfValue, newObj.HalfValue);
            Assert.Equal(obj.NativeInt, newObj.NativeInt);
            Assert.Equal(obj.NativeUInt, newObj.NativeUInt);
            Assert.Equal(obj.Signed128, newObj.Signed128);
            Assert.Equal(obj.Unsigned128, newObj.Unsigned128);
            Assert.Equal(obj.Date, newObj.Date);
            Assert.Equal(obj.Time, newObj.Time);
            Assert.Equal(obj.Duration, newObj.Duration);
            Assert.Equal(obj.Id, newObj.Id);
        }

        [Fact]
        public void Test_NullableScalarTypes_AddOneBytePresenceFlag()
        {
            var scalarBuffer = new DoubleSizeHolder
            {
                Value = 12.5
            }.SerializeObject();
            var nullNullableBuffer = new NullableDoubleSizeHolder().SerializeObject();
            var valuedNullableBuffer = new NullableDoubleSizeHolder
            {
                Value = 12.5
            }.SerializeObject();

            Assert.Equal(sizeof(double), scalarBuffer.Length);
            Assert.Single(nullNullableBuffer);
            Assert.Equal(sizeof(bool) + sizeof(double), valuedNullableBuffer.Length);
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

            var buffer = obj.SerializeObject();
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

            var buffer = obj.SerializeObject();
            var newObj = buffer.DeserializeObject<ConcreteCollectionHolder>();

            Assert.NotNull(newObj.Values);
            Assert.Equal([2, 4, 8], newObj.Values);
        }

        [Fact]
        public void Test_SerializeNetObjectThroughInterface_UsesRuntimeType()
        {
            INetObject packet = new TestPacket
            {
                TaskId = 19,
                Message = "runtime"
            };

            var buffer = packet.Serialize(123);
            var newPacket = buffer.Deserialize<TestPacket>();

            Assert.Equal(19, newPacket.TaskId);
            Assert.Equal("runtime", newPacket.Message);
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

            var firstBuffer = first.SerializeObject();
            var secondBuffer = second.SerializeObject();

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

    public class ExtendedScalarTypes
    {
        public Half HalfValue { get; set; }
        public IntPtr NativeInt { get; set; }
        public UIntPtr NativeUInt { get; set; }
        public Int128 Signed128 { get; set; }
        public UInt128 Unsigned128 { get; set; }
        public DateOnly Date { get; set; }
        public TimeOnly Time { get; set; }
        public TimeSpan Duration { get; set; }
        public Guid Id { get; set; }
    }

    public class NullableScalarTypes
    {
        public double? RemainingSeconds { get; set; }
        public int? Count { get; set; }
        public DateTime? SnapshotTime { get; set; }
        public DateTimeOffset? SendTime { get; set; }
        public Half? HalfValue { get; set; }
        public IntPtr? NativeInt { get; set; }
        public UIntPtr? NativeUInt { get; set; }
        public Int128? Signed128 { get; set; }
        public UInt128? Unsigned128 { get; set; }
        public DateOnly? Date { get; set; }
        public TimeOnly? Time { get; set; }
        public TimeSpan? Duration { get; set; }
        public Guid? Id { get; set; }
    }

    public class DoubleSizeHolder
    {
        public double Value { get; set; }
    }

    public class NullableDoubleSizeHolder
    {
        public double? Value { get; set; }
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
        [NetFieldOffset(0, 4)] public byte Value { get; set; }
    }
}

namespace CodeWF.NetWeaver.Tests.BitFieldCacheCases.Two
{
    public class DuplicateName
    {
        [NetFieldOffset(0, 12)] public ushort Value { get; set; }
    }
}
