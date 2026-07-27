namespace Img2PDF.App.State;

// Bounded LIFO stack — pushing past capacity silently evicts the oldest entry
// rather than growing unbounded, per the spec's "at least 20 levels" requirement.
public sealed class UndoStack<T>
{
    private readonly int _capacity;
    private readonly LinkedList<T> _items = new();

    public UndoStack(int capacity = 20)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count => _items.Count;

    public void Push(T item)
    {
        _items.AddLast(item);

        if (_items.Count > _capacity)
        {
            _items.RemoveFirst();
        }
    }

    public bool TryPop(out T? item)
    {
        if (_items.Count == 0)
        {
            item = default;
            return false;
        }

        item = _items.Last!.Value;
        _items.RemoveLast();
        return true;
    }

    public void Clear() => _items.Clear();
}
