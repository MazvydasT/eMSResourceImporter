using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace eMSResourceImporter
{
    public static class AJTManager
    {
        private static readonly Regex titleRegex = new Regex(@"(.+?)/(\d+)-?(.*)", RegexOptions.Compiled);
        private static readonly Regex attributeRegex = new Regex("(Type|Key|Value)=\"(.*?)\"(?:(?:\\s+(?:Type|Key|Value)=\".*$)|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex resRegex = new Regex(@"^RES\d{7}.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static ConcurrentDictionary<Item, byte> GetItems(string pathToAJT)
        {
            var items = new ConcurrentDictionary<Item, byte>();

            if (!File.Exists(pathToAJT))
                return items;

            else
                Logger.Log(Logger.LogType.Info, $"Reading.", new MessageDetails() { FilePath = pathToAJT });

            var tasks = new List<Task>();

            var nodeCounter = 0;

            using (var streamReader = File.OpenText(pathToAJT))
            {


                Item currentItem = null;
                var itemHierarchyTracker = new Dictionary<int, Item>();

                int orderCounter = 0;
                int lineCounter = 0;

                while (!streamReader.EndOfStream)
                {
                    var line = streamReader.ReadLine().Trim();
                    ++lineCounter;

                    if (line.StartsWith("#")) continue;

                    if (Utils.NewItemRegex.IsMatch(line))
                    {
                        var lineSegments = Utils.SpaceRegex.Split(line);
                        var titleSegments = string.Join(" ", lineSegments.Skip(2)).Trim(new char[] { '"' }).Trim(new char[] { '_' }).Split(new char[] { '_' });

                        var level = int.Parse(lineSegments[0]);
                        var previousLevel = level - 1;

                        var itemType = lineSegments[1].ToUpper();

                        var number = titleSegments[0].Trim();
                        var name = string.Join(" ", titleSegments.Skip(1)).Trim();

                        if (currentItem != null)
                        {
                            var currentItemIsDescendantOfResource = currentItem.IsDescendantOfResource();

                            if (currentItemIsDescendantOfResource)
                                items.TryRemove(currentItem, out byte _);
                        }

                        currentItem = new Item()
                        {
                            SourceFile = pathToAJT,
                            LineInSource = lineCounter,

                            OrderNumber = ++orderCounter,

                            SourceLines = new List<string>() { line },

                            IsPart = itemType == "PRT",
                            IsSub = itemType == "SUB",

                            //IsResource = resRegex.IsMatch(number),

                            Number = number,
                            Name = name
                        };

                        ++nodeCounter;

                        items.TryAdd(currentItem, 0);

                        if (!itemHierarchyTracker.ContainsKey(level))
                            itemHierarchyTracker.Add(level, null);

                        itemHierarchyTracker[level] = currentItem;

                        if (itemHierarchyTracker.ContainsKey(previousLevel))
                        {
                            currentItem.Parent = itemHierarchyTracker[previousLevel];
                            currentItem.Parent.Children.TryAdd(currentItem, 0);
                        }
                    }

                    else if (currentItem != null)
                    {
                        currentItem.SourceLines.Add(line);

                        if (line.StartsWith("ATTR", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!line.StartsWith("ATTR_H", StringComparison.OrdinalIgnoreCase))
                                currentItem.SourceLines.RemoveAt(currentItem.SourceLines.Count - 1);

                            var attributeParts = new Dictionary<string, string>(3);

                            var match = attributeRegex.Match(line);

                            while (match.Success)
                            {
                                var groups = match.Groups;
                                var valueGroup = groups[2];

                                attributeParts.Add(groups[1].Value.ToUpper(), valueGroup.Value);
                                match = attributeRegex.Match(line, valueGroup.Index);
                            }

                            if (attributeParts.ContainsKey("KEY") && attributeParts.ContainsKey("VALUE"))
                            {
                                var key = attributeParts["KEY"].ToUpper();
                                var value = attributeParts["VALUE"].Trim();

                                switch (key)
                                {
                                    case "ITEM NAME":
                                        {
                                            if (value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                                            {
                                                Logger.Log(Logger.LogType.Warning, "Wrong attribute value.", new MessageDetails()
                                                {
                                                    SourceFilePath = currentItem.SourceFile,
                                                    LineNumbe = lineCounter,
                                                    AttributeName = "Item Name",
                                                    AttributeValue = "N/A"
                                                });
                                                break;
                                            }

                                            if (value.EndsWith("(View)", StringComparison.OrdinalIgnoreCase))
                                                value = value.Substring(0, value.Length - 6).Trim();

                                            var titleMatch = titleRegex.Match(value);

                                            if (titleMatch.Success)
                                            {
                                                currentItem.Number = titleMatch.Groups[1].Value.Trim();

                                                if (int.TryParse(titleMatch.Groups[2].Value.Trim(), out int revision))
                                                    currentItem.Revision = revision;

                                                currentItem.Name = titleMatch.Groups[3].Value.Trim();
                                            }

                                            break;
                                        }

                                    case "ITEM OBJECT TYPE":
                                        {
                                            if (value.Equals("N/A", StringComparison.OrdinalIgnoreCase))
                                            {
                                                Logger.Log(Logger.LogType.Warning, "Wrong attribute value.", new MessageDetails()
                                                {
                                                    SourceFilePath = currentItem.SourceFile,
                                                    LineNumbe = lineCounter,
                                                    AttributeName = "Item Object Type",
                                                    AttributeValue = "N/A"
                                                });

                                                break;
                                            }

                                            currentItem.IsResource = currentItem.IsResource || (!value.Equals("F_Program", StringComparison.OrdinalIgnoreCase) && !value.Equals("F_GenericObj", StringComparison.OrdinalIgnoreCase));

                                            Logger.Log(Logger.LogType.Info, "Object type.", new MessageDetails()
                                            {
                                                AttributeName = "Item Object Type",
                                                AttributeValue = value,
                                                SourceFilePath = currentItem.SourceFile,
                                                LineNumbe = lineCounter,
                                                ItemNumber = currentItem.Number
                                            });

                                            break;
                                        }
                                }
                            }
                        }

                        else if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                        {
                            var filePath = line.Substring(4).Trim().Trim(new char[] { '"' });

                            if (filePath.EndsWith("NOT_AVAILABLE.jt", StringComparison.OrdinalIgnoreCase))
                            {
                                currentItem.IsPart = false;
                                continue;
                            }

                            if (!string.IsNullOrWhiteSpace(filePath))
                            {
                                string fullFilePath = Path.GetFullPath(Uri.TryCreate(filePath, UriKind.Absolute, out Uri _) ? filePath : Path.Combine(Path.GetDirectoryName(pathToAJT), filePath));

                                if (!currentItem.IsPart)
                                {
                                    var itemToAttachChildrenTo = currentItem.IsSub ? currentItem.Parent : currentItem;

                                    if (currentItem.IsSub)
                                    {
                                        currentItem.Parent.Children.TryRemove(currentItem, out byte _);
                                        items.TryRemove(currentItem, out byte _);
                                    }

                                    tasks.Add(Task.Run(() =>
                                    {
                                        var pathToChildAJTFile = Path.ChangeExtension(fullFilePath, "ajt");

                                        if(!File.Exists(pathToChildAJTFile))
                                        {
                                            Logger.Log(Logger.LogType.Warning, "File does not exist.", new MessageDetails() { FilePath = pathToChildAJTFile, SourceFilePath = pathToAJT, LineNumbe = lineCounter });
                                            return;
                                        }

                                        var childItems = GetItems(pathToChildAJTFile);

                                        foreach (var childItem in childItems.Keys)
                                        {
                                            items.TryAdd(childItem, 0);
                                        }

                                        var rootChildItems = childItems.Keys.Where(item =>
                                        {
                                            if (item.Parent == null)
                                            {
                                                item.Parent = itemToAttachChildrenTo;
                                                return true;
                                            }

                                            return false;
                                        });

                                        if (itemToAttachChildrenTo != null)
                                            foreach (var rootChildItem in rootChildItems)
                                            {
                                                itemToAttachChildrenTo.Children.TryAdd(rootChildItem, 0);
                                            }
                                    }));
                                }

                                else
                                    currentItem.FilePath = fullFilePath;
                            }
                        }
                    }
                }
            }

            Logger.Log(Logger.LogType.Info, $"Nodes read from file.", new MessageDetails() { FilePath = pathToAJT, Count = nodeCounter });

            Task.WaitAll(tasks.ToArray());

            return items;
        }
    }
}