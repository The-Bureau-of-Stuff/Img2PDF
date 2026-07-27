using Img2PDF.App.State;

namespace Img2PDF.App.Tests;

public class UndoStackTests
{
    [Fact]
    public void TryPop_EmptyStack_ReturnsFalse()
    {
        var stack = new UndoStack<int>();

        var result = stack.TryPop(out var item);

        Assert.False(result);
        Assert.Equal(0, item);
    }

    [Fact]
    public void Push_Then_TryPop_ReturnsLastItemFirst()
    {
        var stack = new UndoStack<string>();
        stack.Push("first");
        stack.Push("second");

        stack.TryPop(out var item);

        Assert.Equal("second", item);
        Assert.Equal(1, stack.Count);
    }

    [Fact]
    public void Push_BeyondCapacity_EvictsOldest()
    {
        var stack = new UndoStack<int>(capacity: 3);
        stack.Push(1);
        stack.Push(2);
        stack.Push(3);
        stack.Push(4);

        Assert.Equal(3, stack.Count);
        stack.TryPop(out var top);
        stack.TryPop(out var second);
        stack.TryPop(out var third);
        Assert.Equal(4, top);
        Assert.Equal(3, second);
        Assert.Equal(2, third);
        Assert.False(stack.TryPop(out _));
    }

    [Fact]
    public void Clear_RemovesAllItems()
    {
        var stack = new UndoStack<int>();
        stack.Push(1);
        stack.Push(2);

        stack.Clear();

        Assert.Equal(0, stack.Count);
        Assert.False(stack.TryPop(out _));
    }

    [Fact]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UndoStack<int>(capacity: 0));
    }
}
