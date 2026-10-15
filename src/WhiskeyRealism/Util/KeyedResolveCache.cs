namespace WhiskeyRealism.Util
{
    // One cached lookup result keyed by (id, list identity), for a resolver whose answer
    // is fixed while that key holds. A found value is always kept; a miss is kept only
    // when the resolver calls it settled (it read initialized data and did not fail), so
    // a miss caused by not-yet-built data or an exception is retried on the next call.
    // Storing replaces the entry, so a call with another key re-resolves.
    internal sealed class KeyedResolveCache<T> where T : class
    {
        private int _id = int.MinValue;
        private object _list;
        private T _value;
        private bool _resolved;

        internal bool TryGet(int id, object list, out T value)
        {
            if (_resolved && id == _id && ReferenceEquals(list, _list))
            {
                value = _value;
                return true;
            }
            value = null;
            return false;
        }

        internal void Store(int id, object list, T value, bool settled)
        {
            _id = id;
            _list = list;
            _value = value;
            _resolved = value != null || settled;
        }

        internal void Reset()
        {
            _id = int.MinValue;
            _list = null;
            _value = null;
            _resolved = false;
        }
    }
}
