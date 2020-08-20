using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace eMSResourceImporter
{
    public class Item
    {
        public int OrderNumber { get; set; } = 0;

        public string SourceFile { get; set; } = null;
        public int LineInSource { get; set; } = 0;
        public List<string> SourceLines { get; set; } = null;

        public string Id { get; set; } = null;

        public bool IsPart { get; set; } = false;
        public bool IsSub { get; set; } = false;
        public bool IsResource { get; set; } = false;

        public string Number { get; set; } = null;
        public string Name { get; set; } = null;

        public int Revision { get; set; } = 0;

        public Item Parent { get; set; } = null;
        //public ConcurrentDictionary<Item, Item> Children { get; } = new ConcurrentDictionary<Item, Item>();
        public ConcurrentDictionary<Item, byte> Children { get; } = new ConcurrentDictionary<Item, byte>();

        public string FilePath { get; set; } = null;

        private int level = -1;
        public int GetLevel(bool refreshCache = false)
        {
            if (refreshCache)
                level = -1;

            if (level == -1)
                level = Parent == null ? 0 : (Parent.GetLevel(refreshCache) + 1);

            return level;
        }

        private Nullable<bool> isDescendantOfResource = null;
        public bool IsDescendantOfResource(bool refreshCache = false)
        {
            if (refreshCache)
                isDescendantOfResource = null;

            if (isDescendantOfResource == null)
                isDescendantOfResource = Parent != null && (Parent.IsResource || Parent.IsDescendantOfResource(refreshCache));

            return isDescendantOfResource.Value;
        }
    }
}