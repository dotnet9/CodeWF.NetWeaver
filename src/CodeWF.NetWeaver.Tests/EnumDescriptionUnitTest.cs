using CodeWF.NetWeaver.Tests.Extensions;
using CodeWF.NetWeaver.Tests.Models;
using Xunit;

namespace CodeWF.NetWeaver.Tests
{
    public class EnumDescriptionUnitTest
    {
        [Fact]
        public void Test_GetEnumDescription_Success()
        {
            var usage = PowerUsage.High;
            var alarmStatus = AlarmStatus.OverLimit | AlarmStatus.UserChanged;

            var usageDescription = usage.Description();
            var alarmStatusDescription = alarmStatus.Description();

            Assert.Equal("High", usageDescription);
            Assert.Equal("OverLimit,User Changed", alarmStatusDescription);

            alarmStatus = AlarmStatus.Normal;
            alarmStatusDescription = alarmStatus.Description();
            Assert.Equal("Normal", alarmStatusDescription);
        }
    }
}
