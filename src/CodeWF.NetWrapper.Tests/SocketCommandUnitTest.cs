using CodeWF.NetWeaver;
using CodeWF.NetWeaver.Base;
using CodeWF.NetWrapper.Commands;

namespace CodeWF.NetWrapper.Tests;

public class SocketCommandUnitTest
{
    [Fact]
    public void Test_TryGetCommand_ReturnsCommand_WhenTypeMatches()
    {
        var source = new TestCommand
        {
            Value = 42,
            Name = "matched"
        };
        var command = CreateSocketCommand(source);

        var success = command.TryGetCommand<TestCommand>(out var result);

        Assert.True(success);
        Assert.NotNull(result);
        Assert.Equal(source.Value, result.Value);
        Assert.Equal(source.Name, result.Name);
    }

    [Fact]
    public void Test_TryGetCommand_ReturnsFalse_WhenTypeDoesNotMatch()
    {
        var command = CreateSocketCommand(new TestCommand
        {
            Value = 7,
            Name = "other"
        });

        var success = command.TryGetCommand<OtherTestCommand>(out var result);

        Assert.False(success);
        Assert.Null(result);
    }

    private static SocketCommand CreateSocketCommand<T>(T value) where T : INetObject
    {
        var buffer = value.Serialize(1001);
        var readIndex = 0;
        Assert.True(buffer.ReadHead(ref readIndex, out var headInfo));
        return new SocketCommand(headInfo, buffer);
    }

    [NetHead(65001, 1)]
    private class TestCommand : INetObject
    {
        public int Value { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    [NetHead(65002, 1)]
    private class OtherTestCommand : INetObject
    {
        public int Value { get; set; }
    }
}
