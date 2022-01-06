namespace Shimakaze.Analyzers;

static class MapUtils
{
    public static TValue? GetOrDefault<TKey, TValue>(this IDictionary<TKey, TValue> map, TKey key) => map.TryGetValue(key, out TValue value) ? value : default;
}