namespace ImageViewer.Core.Session;

/// <summary>
/// Потокобезопасный кэш «последних использованных» (LRU) фиксированного размера.
/// Используется для хранения уже декодированных соседних изображений — переход вперёд/назад становится мгновенным.
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();
    private readonly Lock _sync = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
        _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer);
    }

    public int Capacity => _capacity;

    public int Count
    {
        get { lock (_sync) return _map.Count; }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Помечаем как недавно использованный
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public bool Contains(TKey key)
    {
        lock (_sync) return _map.ContainsKey(key);
    }

    public void Set(TKey key, TValue value)
    {
        lock (_sync)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                _order.Remove(existing);
                _map.Remove(key);
            }

            var node = new LinkedListNode<KeyValuePair<TKey, TValue>>(new(key, value));
            _order.AddFirst(node);
            _map[key] = node;

            while (_map.Count > _capacity)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    public bool Remove(TKey key)
    {
        lock (_sync)
        {
            if (!_map.Remove(key, out var node)) return false;
            _order.Remove(node);
            return true;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _map.Clear();
            _order.Clear();
        }
    }

    /// <summary>Ключи от самого «свежего» к самому старому.</summary>
    public IReadOnlyList<TKey> Keys
    {
        get { lock (_sync) return _order.Select(p => p.Key).ToList(); }
    }
}
