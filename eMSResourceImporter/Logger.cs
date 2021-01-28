using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace eMSResourceImporter
{
    public class MessageDetails
    {
        public string FilePath { get; set; } = null;
        public string SourceFilePath { get; set; } = null;
        public int? LineNumber { get; set; } = null;
        public int? Count { get; set; } = null;
        public string ItemNumber { get; set; } = null;
        public string ItemId { get; set; } = null;
        public string AttributeName { get; set; } = null;
        public string AttributeValue { get; set; } = null;
    }

    public static class Logger
    {
        public static ConcurrentDictionary<Task, byte> WriteTasks { get; } = new ConcurrentDictionary<Task, byte>();

        public enum LogType
        {
            Info,
            Warning,
            Error
        }

        public static bool Verbose { get; set; } = false;

        static int firstLog = 1;

        public static void Log(LogType type, string message, MessageDetails messageDetails = null)
        {
            if (!Verbose && type == LogType.Info) return;

            if (Interlocked.CompareExchange(ref firstLog, 0, 1) == 1)
                Console.Error.WriteLine($"Timestamp,Type,Message,Count,Item number,Item id,Attribute name,Attribute value,Filepath,Source file,Line number");

            var md = messageDetails;

            var writeTask = Console.Error.WriteLineAsync($"{DateTime.Now},{type},{Utils.ToCSV(message)},{md?.Count},{Utils.ToCSV(md?.ItemNumber)},{Utils.ToCSV(md?.ItemId)},{Utils.ToCSV(md?.AttributeName)},{Utils.ToCSV(md?.AttributeValue)},{Utils.ToCSV(md?.FilePath)},{Utils.ToCSV(md?.SourceFilePath)},{md?.LineNumber}");

            WriteTasks.TryAdd(writeTask, 0);

            writeTask.ContinueWith(task => WriteTasks.TryRemove(task, out byte _));
        }
    }
}