using System.Collections.Generic;
using DuplicateNameOne = CodeWF.NetWeaver.Tests.CacheCases.One.DuplicateName;
using DuplicateNameTwo = CodeWF.NetWeaver.Tests.CacheCases.Two.DuplicateName;
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
