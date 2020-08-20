using NDesk.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace eMSResourceImporter
{
    class JTCreationData
    {
        public string JTPath { get; set; }
        public Item ResourceItem { get; set; }
    }

    class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            args = new[]
            {
                //@"-i=K:\digitalbuck\MANU_LIB\LDI\JLR0479420.ajt",
                //@"-o=C:\Users\mtadara1\Desktop\JLR0479420_TF_COMMON_TOOLING_LIBRARY.xml",

                @"-i=K:\digitalbuck\MANU_LIB\LDI\PRG-MFG-LIB-000.ajt",
                @"-o=C:\Users\mtadara1\Desktop\PRG-MFG-LIB-000_MANUFACTURING_RESOURCE_LIBRARIES.xml",

                @"-s=P:\sys_root",
                @"-c=P:\sys_root\LIBRARIES\MFG-LIB",

                //@"-s=C:\Users\mtadara1\Desktop\sysroot",
                //@"-c=C:\Users\mtadara1\Desktop\sysroot",

                @"-a=C:\Program Files\Siemens\JtUtilities\12_4\bin64\asciitojt.exe",

                "-p",

                "-v",

                @"-l=C:\Users\mtadara1\Desktop\log.csv",

                //"--skipJTCreation",
                //"--forceJTCreation",

                //"-?",

                //"--listEncodings"
            };
#endif

            string ajtFilePath = null;
            string sysRootPath = null;
            string cojtFolder = null;
            string ajt2jtExePath = null;
            string outputPath = null;
            string logPath = null;

            bool includeEmptyBranches = false;

            bool prettyPrint = false;
            string encodingName = null;

            bool skipJTCreation = false;
            bool forceJTCreation = false;

            bool showHelp = false;
            bool listEncodings = false;

            var options = new OptionSet()
            {
                { "i|ajtInputFile=", "Mandatory: Path to .ajt input {FILE}", v => ajtFilePath = Utils.PathToUNC(v) },
                { "s|sysRoot=", "Mandatory: Path to sys_root {DIRECTORY}", v => sysRootPath = Utils.PathToUNC(v) },
                { "c|cojtFolder=", "Mandator: Path to {DIRECTORY} for .cojt\n(must be under sys_root)", v => cojtFolder = Utils.PathToUNC(v) },
                { "a|asciitojt=", "Mandatory: Path to {EXE} for AJT to JT conversion", v => ajt2jtExePath = v },

                { "includeEmptyBranches", "Includes branches without CAD nodes.", v => includeEmptyBranches = v != null },

                { "o|outputFile=","Redirects stdout to {FILE}", v => outputPath = v },
                { "l|logFile=","Redirects stderr to {FILE}", v => logPath = v },

                { "v|verbose","Enable verbose logging", v => Logger.Verbose = v != null },
                { "p|prettyPrint", "Indents output XML", v => prettyPrint = v != null },
                { "e|encoding=", "Changes output {ENCODING} from UTF-8", v => encodingName = v },

                { "skipJTCreation", "Skips creating JT files", v => skipJTCreation = v != null },
                { "forceJTCreation", "Overwrites exisitng JT files", v => forceJTCreation = v != null },

                { "h|?|help", "Shows this help message", v => showHelp = v != null },
                { "listEncodings", "Lists available encodings", v => listEncodings = v != null },
            };

            List<string> extra;
            try
            {
                extra = options.Parse(args);
            }

            catch (OptionException e)
            {
                Logger.Log(Logger.LogType.Error, string.Join("\n", new[] {
                    $"{Process.GetCurrentProcess().ProcessName} {string.Join(" ", args)}",
                    e.Message
                }));

                Console.WriteLine();
                Utils.ShowHelp(options);
                return;
            }

            if (showHelp)
            {
                Utils.ShowHelp(options);
                return;
            }

            if (listEncodings)
            {
                foreach (var name in Encoding.GetEncodings().Select(e => e.Name).OrderBy(n => n))
                {
                    Console.WriteLine(name);
                }

                return;
            }

            var encoding = new UTF8Encoding(false);
            if (!string.IsNullOrWhiteSpace(encodingName))
            {
                try
                {
                    var newEncoding = Encoding.GetEncoding(encodingName);
                }

                catch (Exception e)
                {
                    Logger.Log(Logger.LogType.Error, e.Message);
                    return;
                }
            }
            Console.OutputEncoding = encoding;

            if (!string.IsNullOrWhiteSpace(logPath))
            {
                try
                {
                    Console.SetError(TextWriter.Synchronized(new StreamWriter(File.Open(logPath, File.Exists(logPath) ? FileMode.Append : FileMode.CreateNew, FileAccess.Write, FileShare.Read), encoding, ushort.MaxValue)));
                }

                catch (Exception e)
                {
                    Logger.Log(Logger.LogType.Error, e.Message, new MessageDetails() { FilePath = logPath });
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(ajtFilePath) || !File.Exists(ajtFilePath))
            {
                Logger.Log(Logger.LogType.Error, "Path to .ajt file is not valid.");
                showHelp = true;
            }

            if (string.IsNullOrWhiteSpace(sysRootPath) || !Directory.Exists(sysRootPath))
            {
                Logger.Log(Logger.LogType.Error, "Path to sys_root is not valid.");
                showHelp = true;
            }

            if (string.IsNullOrWhiteSpace(cojtFolder) || !Directory.Exists(cojtFolder))
            {
                Logger.Log(Logger.LogType.Error, "Path to directory for .cojt is not valid.");
                showHelp = true;
            }

            if (!skipJTCreation && (string.IsNullOrWhiteSpace(ajt2jtExePath) || !File.Exists(ajt2jtExePath)))
            {
                Logger.Log(Logger.LogType.Error, "Path to .exe for AJT to JT conversion is not valid.");
                showHelp = true;
            }

            if (cojtFolder != null && sysRootPath != null && !cojtFolder.StartsWith(sysRootPath))
            {
                Logger.Log(Logger.LogType.Error, "Path to directory for .cojt must be under sys_root.");
                showHelp = true;
            }

            if (showHelp)
            {
                Utils.ShowHelp(options);
                return;
            }

            if(!File.Exists(ajtFilePath))
            {
                Logger.Log(Logger.LogType.Warning, "File does not exist.", new MessageDetails() { FilePath = ajtFilePath });
                return;
            }

            var items = AJTManager.GetItems(ajtFilePath);
            if (items.Count == 0)
            {
                Logger.Log(Logger.LogType.Error, "No items were extracted from provided file.", new MessageDetails() { SourceFilePath = ajtFilePath });
                return;
            }

            var itemsWithInvalidNumber = items.Keys.Where(item => string.IsNullOrWhiteSpace(item.Number));
            foreach (var itemWithInvalidNumber in itemsWithInvalidNumber)
            {
                Logger.Log(Logger.LogType.Warning, "Excluding this branch. Not possible to extract valid part/assembly number.", new MessageDetails() { SourceFilePath = itemWithInvalidNumber.SourceFile, LineNumbe = itemWithInvalidNumber.LineInSource });
                
                ItemsManager.RemoveItemAndDescendents(itemWithInvalidNumber, items);
            }

            if (!includeEmptyBranches)
            {
                var endItems = items.Keys.Where(item => item.Children.Count == 0 && string.IsNullOrWhiteSpace(item.FilePath));
                foreach (var endItem in endItems)
                {
                    ItemsManager.RemoveEmptyBrach(endItem, items);
                }
            }

            var orderedItems = items.Keys
                .Where(item => !item.IsDescendantOfResource())
                .OrderByDescending(item => item.GetLevel())
                .ToArray();

            var sysRootRelativeCADFolder = cojtFolder.Replace(sysRootPath, "");

            if (!sysRootRelativeCADFolder.StartsWith(@"\") && !sysRootRelativeCADFolder.StartsWith("/"))
                sysRootRelativeCADFolder = @"\" + sysRootRelativeCADFolder;

            var fileCreationTasks = new ConcurrentBag<Task>();

            var fileTracker = new ConcurrentDictionary<string, string>();

            var resources = orderedItems.Where(item => item.IsResource && !item.IsPart);

            Parallel.ForEach(resources, resourceItem =>
            {
                resourceItem.IsPart = true;

                var fileName = resourceItem.Number;

                if (resourceItem.Revision > 0)
                    fileName += $"{-resourceItem.Revision}";

                resourceItem.FilePath = Path.Combine($"#{sysRootRelativeCADFolder}", $"{fileName}.cojt");

#if DEBUG
                /*if (resourceItem.Number == "RES0540155")
                {
                    var test = resourceItem;
                }

                else
                    return;*/
#endif

                if (!skipJTCreation)
                {
                    var fullJTFilePath = Path.Combine(cojtFolder, $"{fileName}.cojt", $"{fileName}.jt");

                    if (fileTracker.TryAdd(fullJTFilePath, null))
                    {
                        if (forceJTCreation || !File.Exists(fullJTFilePath))
                        {
                            fileCreationTasks.Add(Task.Factory.StartNew(data =>
                            {
                                var jtFilePath = ((JTCreationData)data).JTPath;
                                var resource = ((JTCreationData)data).ResourceItem;

                                var ajtSourceLines = Utils.GetAJTSource(resource, 0);

                                string tempAJTFilePath = null;
                                string tempJTFilePath = null;

                                try
                                {
                                    tempAJTFilePath = Path.GetTempFileName();
                                    tempJTFilePath = Path.ChangeExtension(tempAJTFilePath, ".jt");

                                    File.WriteAllLines(tempAJTFilePath, ajtSourceLines);

                                    var process = Process.Start(new ProcessStartInfo()
                                    {
                                        FileName = ajt2jtExePath,
                                        Arguments = $"\"{tempAJTFilePath}\" \"{tempJTFilePath}\"",
                                        UseShellExecute = false,
                                        CreateNoWindow = true,
                                        RedirectStandardError = true
                                    });

                                    var error = process.StandardError.ReadToEnd().Trim();

                                    if (!string.IsNullOrWhiteSpace(error))
                                        throw new Exception(error);

                                    process.WaitForExit();

                                    var outputDirectory = Path.GetDirectoryName(jtFilePath);

                                    if (!Directory.Exists(outputDirectory))
                                        Directory.CreateDirectory(outputDirectory);

                                    File.Copy(tempJTFilePath, jtFilePath, true);

                                    Logger.Log(Logger.LogType.Info, $"JT file created successfully.", new MessageDetails() { FilePath = jtFilePath });
                                }

                                catch (Exception e)
                                {
                                    Logger.Log(Logger.LogType.Warning, e.Message, new MessageDetails() { FilePath = jtFilePath });
                                }

                                finally
                                {
                                    if (File.Exists(tempAJTFilePath))
                                    {
                                        try
                                        {
                                            File.Delete(tempAJTFilePath);
                                        }

                                        catch (Exception e)
                                        {
                                            Logger.Log(Logger.LogType.Warning, e.Message, new MessageDetails() { FilePath = tempAJTFilePath });
                                        }
                                    }

                                    if (File.Exists(tempJTFilePath))
                                    {
                                        try
                                        {
                                            File.Delete(tempJTFilePath);
                                        }

                                        catch (Exception e)
                                        {
                                            Logger.Log(Logger.LogType.Warning, e.Message, new MessageDetails() { FilePath = tempJTFilePath });
                                        }
                                    }
                                }
                            }, new JTCreationData { JTPath = fullJTFilePath, ResourceItem = resourceItem }, TaskCreationOptions.LongRunning));
                        }

                        else
                            Logger.Log(Logger.LogType.Info, "File already exists. Skipping.", new MessageDetails()
                            {
                                FilePath = fullJTFilePath
                            });
                    }

                    else
                        Logger.Log(Logger.LogType.Info, "File created previously. Skipping.", new MessageDetails()
                        {
                            FilePath = fullJTFilePath
                        });
                }
            });

            var streamForXMLWriter = Console.Out;
            if (!string.IsNullOrWhiteSpace(outputPath))
            {
                try
                {
                    streamForXMLWriter = new StreamWriter(new FileStream(outputPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read), encoding);
                }

                catch (Exception e)
                {
                    Logger.Log(Logger.LogType.Warning, e.Message, new MessageDetails()
                    {
                        FilePath = outputPath
                    });
                    
                    return;
                }
            }

            var externalIdTracker = new HashSet<string>();

            using (var xmlWriter = XmlWriter.Create(streamForXMLWriter, new XmlWriterSettings()
            {
                Encoding = encoding,
                Indent = prettyPrint,
                NewLineChars = "\n",
                IndentChars = "\t"
            }))
            {
                xmlWriter.WriteStartDocument(true);
                xmlWriter.WriteStartElement("Data");
                xmlWriter.WriteStartElement("Objects");

                foreach (var item in orderedItems)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        Logger.Log(Logger.LogType.Warning, $"Item does not have a name.", new MessageDetails()
                        {
                            ItemNumber = item.Number,
                            SourceFilePath = item.SourceFile,
                            LineNumbe = item.LineInSource
                        });

                    if (item.Revision == 0)
                        Logger.Log(Logger.LogType.Warning, $"Item does not have valid revision, '0' is used instead.", new MessageDetails()
                        {
                            ItemNumber = item.Number,
                            SourceFilePath = item.SourceFile,
                            LineNumbe = item.LineInSource
                        });

                    var title = item.Number;

                    if (item.Revision > 0)
                        title += $"/{item.Revision}";

                    if (!string.IsNullOrWhiteSpace(item.Name))
                        title += $"-{item.Name}";

                    var partIdPrefix = "ResLibPart_";

                    if (item.IsPart)
                    {
                        var partExternalId = item.Id ?? (item.Id = $"{partIdPrefix}{item.Number}_{item.Revision}");


                        if (externalIdTracker.Contains(partExternalId))
                        {
                            Logger.Log(Logger.LogType.Info, $"Skipping XML generation. Repeated ExternalId.", new MessageDetails()
                            {
                                ItemNumber = item.Id,
                                ItemId = partExternalId
                            });

                            continue;
                        }

                        var threeDRepId = $"{partIdPrefix}3DRep_{item.Number}";
                        var fileId = $"{partIdPrefix}ReferenceFile_{item.Number}";

                        xmlWriter.WriteStartElement("PmReferenceFile");
                        xmlWriter.WriteAttributeString("ExternalId", fileId);

                        xmlWriter.WriteStartElement("fileName");
                        xmlWriter.WriteString(item.FilePath);
                        xmlWriter.WriteEndElement();

                        xmlWriter.WriteEndElement();



                        xmlWriter.WriteStartElement("Pm3DRep");
                        xmlWriter.WriteAttributeString("ExternalId", threeDRepId);

                        xmlWriter.WriteStartElement("file");
                        xmlWriter.WriteString(fileId);
                        xmlWriter.WriteEndElement();

                        xmlWriter.WriteEndElement();



                        xmlWriter.WriteStartElement("PmToolPrototype");
                        xmlWriter.WriteAttributeString("ExternalId", partExternalId);

                        xmlWriter.WriteStartElement("name");
                        xmlWriter.WriteString(title);
                        xmlWriter.WriteEndElement();

                        xmlWriter.WriteStartElement("catalogNumber");
                        xmlWriter.WriteString(item.Number);
                        xmlWriter.WriteEndElement();

                        xmlWriter.WriteStartElement("TCe_Revision");
                        xmlWriter.WriteString(item.Revision.ToString());
                        xmlWriter.WriteEndElement();

                        xmlWriter.WriteStartElement("threeDRep");
                        xmlWriter.WriteString(threeDRepId);
                        xmlWriter.WriteEndElement();

                        xmlWriter.WriteEndElement();
                    }

                    else
                    {
                        var containerIdPrefix = "ResLibContainer_";
                        var containerExternalId = item.Id;

                        if (containerExternalId == null) {
                            var externalIdCounter = 0;

                            do
                            {
                                containerExternalId = $"{containerIdPrefix}{item.Number}_{++externalIdCounter}";
                            }
                            while (externalIdTracker.Contains(containerExternalId));

                            item.Id = containerExternalId;
                        }

                        externalIdTracker.Add(containerExternalId);

                        xmlWriter.WriteStartElement("PmResourceLibrary");
                        xmlWriter.WriteAttributeString("ExternalId", containerExternalId);

                        xmlWriter.WriteStartElement("name");
                        xmlWriter.WriteString(title);
                        xmlWriter.WriteEndElement();

                        if (item.Children.Count > 0)
                        {
                            xmlWriter.WriteStartElement("children");

                            var orderedChildren = item.Children.Keys.OrderBy(child => child.OrderNumber).Select(child => {
                                var childId = child.Id;

                                if(childId == null)
                                {
                                    if (child.IsPart)
                                        childId = $"{partIdPrefix}{child.Number}_{child.Revision}";

                                    else
                                    {
                                        var externalIdCounter = 0;

                                        do
                                        {
                                            childId = $"{containerIdPrefix}{child.Number}_{++externalIdCounter}";
                                        }
                                        while (externalIdTracker.Contains(childId));
                                    }

                                    child.Id = childId;
                                }

                                return child;
                            });

                            var childExternalIdTracker = new HashSet<string>();

                            foreach (var child in orderedChildren)
                            {
                                if (childExternalIdTracker.Contains(child.Id))
                                {
                                    Logger.Log(Logger.LogType.Info, $"Excluding from 'children' element. Repeated child ExternalId.", new MessageDetails()
                                    {
                                        ItemId = child.Id,
                                        ItemNumber = child.Number
                                    });
                                    continue;
                                }

                                childExternalIdTracker.Add(child.Id);

                                xmlWriter.WriteStartElement("item");
                                xmlWriter.WriteString(child.Id);
                                xmlWriter.WriteEndElement();
                            }

                            xmlWriter.WriteEndElement();
                        }

                        xmlWriter.WriteEndElement();
                    }
                }

                xmlWriter.WriteEndDocument();
            }

            Task.WaitAll(fileCreationTasks.ToArray());

            Task.WaitAll(Logger.WriteTasks.Keys.ToArray());
            Console.Error.Dispose();
        }
    }
}