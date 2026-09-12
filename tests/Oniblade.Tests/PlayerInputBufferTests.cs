using Oniblade.Player;
using Xunit;

namespace Oniblade.Tests;

public class PlayerInputBufferTests
{
    [Fact]
    public void Buffered_Input_Is_Consumed_Within_Window()
    {
        var buf = new PlayerInputBuffer();
        buf.Tick();
        buf.Push(PlayerAction.Attack);

        Assert.True(buf.Consume(PlayerAction.Attack, 8));
    }

    [Fact]
    public void Consume_Is_Exclusive()
    {
        var buf = new PlayerInputBuffer();
        buf.Tick();
        buf.Push(PlayerAction.Attack);

        Assert.True(buf.Consume(PlayerAction.Attack, 8));
        Assert.False(buf.Consume(PlayerAction.Attack, 8));
    }

    [Fact]
    public void Expired_Input_Is_Dropped_By_Tick()
    {
        var buf = new PlayerInputBuffer { DefaultLifetimeFrames = 2 };
        buf.Push(PlayerAction.Dodge);

        buf.Tick();
        buf.Tick();
        buf.Tick();

        Assert.False(buf.Consume(PlayerAction.Dodge, 2));
        Assert.Equal(0, buf.Count);
    }

    [Fact]
    public void Input_Outside_Window_Is_Not_Consumed()
    {
        var buf = new PlayerInputBuffer { DefaultLifetimeFrames = 100 };
        buf.Push(PlayerAction.Dodge);
        for (int i = 0; i < 10; i++)
            buf.Tick();

        // 还在缓冲里，但已经超过"闪避取消窗"2 帧 → 不能被消费
        Assert.Equal(1, buf.Count);
        Assert.False(buf.Consume(PlayerAction.Dodge, 2));
    }

    [Fact]
    public void Age_Reports_Frames_Since_Press()
    {
        var buf = new PlayerInputBuffer();
        buf.Tick();
        buf.Push(PlayerAction.Guard);
        buf.Tick();
        buf.Tick();

        Assert.Equal(2, buf.Age(PlayerAction.Guard));
        Assert.Equal(-1, buf.Age(PlayerAction.OniMagic));
    }

    [Fact]
    public void Has_Does_Not_Consume()
    {
        var buf = new PlayerInputBuffer();
        buf.Tick();
        buf.Push(PlayerAction.ItemUse);

        Assert.True(buf.Has(PlayerAction.ItemUse, 8));
        Assert.True(buf.Consume(PlayerAction.ItemUse, 8));
    }

    [Fact]
    public void Overflow_Keeps_The_Newest_Input()
    {
        var buf = new PlayerInputBuffer();
        buf.Tick();

        for (int i = 0; i < 20; i++)
            buf.Push(PlayerAction.Attack);
        buf.Push(PlayerAction.Dodge);

        Assert.True(buf.Has(PlayerAction.Dodge, 8));
        Assert.Equal(16, buf.Count);
    }
}
