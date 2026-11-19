namespace NimCap.Screenshot.ViewModels;

internal sealed class BoundedHistory<T>(int capacity)
{
    private readonly LinkedList<T> _items = new();
    public int Count => _items.Count;
    public void Push(T item)
    {
        _items.AddLast(item);
        if (_items.Count > capacity) _items.RemoveFirst();
    }
    public T Pop()
    {
        var last = _items.Last ?? throw new InvalidOperationException("History is empty.");
        _items.RemoveLast();
        return last.Value;
    }
    public void Clear() => _items.Clear();
}
