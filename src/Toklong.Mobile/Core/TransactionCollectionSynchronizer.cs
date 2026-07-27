using System.Collections.ObjectModel;

namespace Toklong.Mobile.Core;

public static class TransactionCollectionSynchronizer
{
    public static void Synchronize(
        ObservableCollection<AppTransaction> target,
        IReadOnlyList<AppTransaction> desired)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);

        for (var index = 0; index < desired.Count; index++)
        {
            var desiredItem = desired[index];
            if (index < target.Count &&
                target[index].Id == desiredItem.Id)
            {
                target[index] = desiredItem;
                continue;
            }

            var existingIndex = FindIndex(target, desiredItem.Id, index + 1);
            if (existingIndex >= 0)
            {
                target.Move(existingIndex, index);
                target[index] = desiredItem;
            }
            else
            {
                target.Insert(index, desiredItem);
            }
        }

        while (target.Count > desired.Count)
            target.RemoveAt(target.Count - 1);
    }

    private static int FindIndex(
        IReadOnlyList<AppTransaction> items,
        Guid id,
        int startIndex)
    {
        for (var index = startIndex; index < items.Count; index++)
        {
            if (items[index].Id == id)
                return index;
        }

        return -1;
    }
}
