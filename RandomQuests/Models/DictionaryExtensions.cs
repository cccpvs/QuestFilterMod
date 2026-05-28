public static class DictionaryExtensions
{
    public static TValue GetOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue)
        where TValue : struct
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }
}