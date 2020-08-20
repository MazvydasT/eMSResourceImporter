using NDesk.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace eMSResourceImporter
{
    public static class Utils
    {
        private static readonly Regex driveLetterRegexp = new Regex(@"^([A-Za-z]:).*$", RegexOptions.Compiled);
        public static string PathToUNC(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith(@"\\") || !Path.IsPathRooted(path)) return path;

            path = Path.GetFullPath(path);

            var driveLetterMatch = driveLetterRegexp.Match(path);

            if (driveLetterMatch.Success)
            {
                var drivePath = driveLetterMatch.Groups[1].Value;
                var driveAddress = GetDriveAddress(drivePath);

                if (driveAddress != null)
                {
                    path = path.Replace(drivePath, driveAddress);
                }
            }

            return path;
        }

        private static string GetDriveAddress(string drivePath)
        {
            using (var managementObject = new ManagementObject())
            {
                managementObject.Path = new ManagementPath(string.Format("Win32_LogicalDisk='{0}'", (object)drivePath));

                try
                {
                    return Convert.ToUInt32(managementObject["DriveType"]) == 4 ? Convert.ToString(managementObject["ProviderName"]) : drivePath;
                }

                catch (Exception)
                {
                    return null;
                }
            }
        }

        public static readonly Regex NewItemRegex = new Regex(@"^\d+\s+.+$", RegexOptions.Compiled);
        public static readonly Regex SpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static IEnumerable<string> GetAJTSource(Item item, int level)
        {
            var sourceLines = ((IEnumerable<string>)item.SourceLines ?? new string[0]).Select(sourceLine =>
            {
                if (NewItemRegex.IsMatch(sourceLine))
                    return $"{level} {string.Join(" ", SpaceRegex.Split(sourceLine).Skip(1))}";

                if (sourceLine.StartsWith("File", StringComparison.OrdinalIgnoreCase))
                    return $"File \"{item.FilePath}\"";

                return SpaceRegex.Replace(sourceLine, " ");
            });

            var childrenSourceLines = item.Children.Keys.Select(child => GetAJTSource(child, level + 1)).SelectMany(x => x);

            return sourceLines.Concat(childrenSourceLines);
        }

        public static string ToCSV(string value) => value == null ? null : $"\"{value.Replace("\"", "\"\"")}\"";

        public static void ShowHelp(OptionSet options)
        {
            Console.WriteLine($"Usage: {Process.GetCurrentProcess().ProcessName} [OPTIONS]+");
            Console.WriteLine();
            Console.WriteLine("Options:");
            options.WriteOptionDescriptions(Console.Out);
        }
    }
}
