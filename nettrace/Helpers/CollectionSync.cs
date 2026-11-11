using System.Collections.ObjectModel;

namespace NetTrace.Helpers;

public static class CollectionSync
{
    /// <summary>
    /// reconciles <paramref name="collection"/> with <paramref name="target"/> in place, using
    /// moves, inserts and removals instead of a reset. this keeps selection and scroll position
    /// stable and avoids rebuilding row containers on every refresh.
    /// </summary>
    public static void SyncTo<T>(this ObservableCollection<T> collection, IReadOnlyList<T> target)
        where T : class
    {
        if (target.Count == 0)
        {
            if (collection.Count > 0)
            {
                collection.Clear();
            }

            return;
        }

        // remove items that are no longer wanted
        var wanted = new HashSet<T>(target, ReferenceEqualityComparer.Instance as IEqualityComparer<T>);
        for (int i = collection.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(collection[i]))
            {
                collection.RemoveAt(i);
            }
        }

        var present = new Dictionary<T, int>(collection.Count, ReferenceEqualityComparer.Instance as IEqualityComparer<T>);
        for (int i = 0; i < collection.Count; i++)
        {
            present[collection[i]] = i;
        }

        for (int i = 0; i < target.Count; i++)
        {
            T item = target[i];
            if (i < collection.Count && ReferenceEquals(collection[i], item))
            {
                continue;
            }

            if (present.ContainsKey(item))
            {
                int current = IndexOf(collection, item, i);
                if (current >= 0)
                {
                    collection.Move(current, i);
                    continue;
                }
            }

            collection.Insert(i, item);
        }

        while (collection.Count > target.Count)
        {
            collection.RemoveAt(collection.Count - 1);
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> collection, T item, int start)
        where T : class
    {
        for (int i = start; i < collection.Count; i++)
        {
            if (ReferenceEquals(collection[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
