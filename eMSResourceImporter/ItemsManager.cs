using System.Collections.Concurrent;

namespace eMSResourceImporter
{
    public static class ItemsManager
    {
        public static void RemoveEmptyBrach(Item item, ConcurrentDictionary<Item, byte> items)
        {
            if (item.Children.Count == 0 && string.IsNullOrWhiteSpace(item.FilePath))
            {
                items.TryRemove(item, out byte _);
                item.Parent.Children.TryRemove(item, out byte _);

                RemoveEmptyBrach(item.Parent, items);
            }
        }

        public static void RemoveItemAndDescendents(Item item, ConcurrentDictionary<Item, byte> items)
        {
            items.TryRemove(item, out byte _);
            item.Parent.Children.TryRemove(item, out byte _);
            item.Parent = null;

            foreach (var child in item.Children.Keys)
            {
                RemoveItemAndDescendents(child, items);
            }
        }
    }
}