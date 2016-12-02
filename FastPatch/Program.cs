using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace FastPatch
{
    class Program
    {
        public const int MAX_THREADS = 8;

        static void Main(String[] args)
        {
            Program.DownloadPatch(Environment.CurrentDirectory);
        }

        public static void DownloadPatch(String destinationPath)
        {
            Int32 i = 0;
            DirectoryInfo directory = new DirectoryInfo(destinationPath);

            if (!directory.Exists) directory.Create();

            using (WebClient webClient = new WebClient())
            {
                Console.WriteLine("Loading LauncherInfo");

                LauncherInfo launcherInfo = new LauncherInfo { };

                String[] launcherInfoLines = webClient.DownloadString("http://manifest.robertsspaceindustries.com/Launcher/_LauncherInfo").Split(new Char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in launcherInfoLines)
                {
                    if (line.StartsWith("universes"))
                    {
                        launcherInfo.Universes = line
                            .Split(new String[] { " = ", "," }, StringSplitOptions.RemoveEmptyEntries)
                            .Where(u => u != "universes")
                            .ToDictionary(k => k.Trim().ToLowerInvariant(), v => new Universe { Name = v.Trim() }, StringComparer.InvariantCultureIgnoreCase);
                    }
                    else
                    {
                        var parts = line.Split(new String[] { "_", " = " }, StringSplitOptions.RemoveEmptyEntries);

                        switch (parts[1])
                        {
                            case "universeServer": launcherInfo.Universes[parts[0]].UniverseServer = parts[2]; break;
                            case "version": launcherInfo.Universes[parts[0]].Version = parts[2]; break;
                            case "fileIndex": launcherInfo.Universes[parts[0]].FileIndex = parts[2]; break;
                            default: Console.WriteLine($"Warning - unable to process {line}"); break;
                        }
                    }
                }

                Console.WriteLine("LauncherInfo Loaded");

                if (launcherInfo.Universes.ContainsKey(directory.Name))
                {
                    Universe universe = launcherInfo.Universes[directory.Name];

                    Console.Title = $"Downloading latest {universe.Name} build";

                    Console.WriteLine($"{universe.Name} build found - now downloading");
                    Console.WriteLine();

                    Manifest manifest = webClient.DownloadString(universe.FileIndex).FromJSON<Manifest>();

                    String[] fileList = manifest.FileList;

                    Parallel.ForEach(
                        Enumerable.Range(0, fileList.Length), 
                        new ParallelOptions { MaxDegreeOfParallelism = MAX_THREADS }, 
                        j =>
                        {
                            String file = fileList[j];
                            Boolean success = false;

                            while (!success)
                            {
                                try
                                {
                                    using (WebClient fileClient = new WebClient())
                                    {
                                        FileInfo targetFile = new FileInfo(Path.Combine(destinationPath, file));

                                        if (!targetFile.Directory.Exists) targetFile.Directory.Create();
                                        if (targetFile.Exists) targetFile.Delete();

                                        fileClient.DownloadFile(String.Format("{0}/{1}/{2}", manifest.WebseedUrls[i++ % manifest.WebseedUrls.Length], manifest.KeyPrefix, file), targetFile.FullName);

                                        Console.WriteLine("Completed File: {0}", file);

                                        success = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine("Error Downloading File: {0}", file);

                                    Thread.Sleep(5000);
                                }
                            }
                        });

                    Console.Title = $"Download of {universe.Name} complete";

                    Console.WriteLine();
                    Console.WriteLine($"Download of {universe.Name} complete");

                    FileInfo patcherState = new FileInfo(Path.Combine(destinationPath, @"..\..\Patcher\PatcherState"));

                    if (!patcherState.Exists) Console.WriteLine("Unable to locate PatcherState - you will need to manually update the PatcherState file, or run a verify");

                    if (patcherState.Exists)
                    {
                        var patcherLines = File.ReadAllLines(patcherState.FullName);

                        List<String> outLines = new List<String> { };

                        foreach (String patcherLine in patcherLines)
                        {
                            if (patcherLine.StartsWith($"{universe.Name}_downloadFinished", StringComparison.InvariantCultureIgnoreCase))
                            {
                                outLines.Add($"{universe.Name}_downloadFinished = true");
                            }
                            else if (patcherLine.StartsWith($"{universe.Name}_oldVersion", StringComparison.InvariantCultureIgnoreCase))
                            {
                                outLines.Add($"{universe.Name}_oldVersion = {universe.Version}");
                            }
                            else if (patcherLine.StartsWith($"{universe.Name}_version", StringComparison.InvariantCultureIgnoreCase))
                            {
                                outLines.Add($"{universe.Name}_version = {universe.Version}");
                            }
                            else if (patcherLine.StartsWith($"{universe.Name}_fileIndex", StringComparison.InvariantCultureIgnoreCase))
                            {
                                outLines.Add($"{universe.Name}_fileIndex = {universe.FileIndex}");
                            }
                            else if (patcherLine.StartsWith($"{universe.Name}_installed", StringComparison.InvariantCultureIgnoreCase))
                            {
                                outLines.Add($"{universe.Name}_installed = true");
                            }
                            else if (patcherLine.StartsWith($"{universe.Name}_checking", StringComparison.InvariantCultureIgnoreCase))
                            {
                                outLines.Add($"{universe.Name}_checking = false");
                            }
                            else if (patcherLine.StartsWith($"{universe.Name}_universeServer", StringComparison.InvariantCultureIgnoreCase))
                            {
                                outLines.Add($"{universe.Name}_universeServer = {universe.UniverseServer}");
                            }
                            else
                            {
                                outLines.Add(patcherLine);
                            }
                        }

                        patcherState.Delete();

                        File.AppendAllLines(patcherState.FullName, outLines);
                    }
                }
                
                Thread.Sleep(10000);
            }
        }
    }

    public class Manifest
    {
        [JsonProperty(PropertyName = "byte_count_total")]
        public UInt64 ByteCountTotal { get; set; }

        [JsonProperty(PropertyName = "file_count_total")]
        public UInt32 FileCountTotal { get; set; }

        [JsonProperty(PropertyName = "file_list")]
        public String[] FileList { get; set; }

        [JsonProperty(PropertyName = "key_prefix")]
        public String KeyPrefix { get; set; }

        [JsonProperty(PropertyName = "webseed_urls")]
        public String[] WebseedUrls { get; set; }
    }

    public class LauncherInfo
    {
        public Dictionary<String, Universe> Universes { get; set; }
    }

    public class Universe
    {
        public String Name { get; set; }
        public String UniverseServer { get; set; }
        public String Version { get; set; }
        public String FileIndex { get; set; }
    }
}