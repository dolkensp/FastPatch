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
        public const int MAX_THREADS = 1;

        static void Main(String[] args) => Program.Run();

        private static LauncherInfo GetLauncherInfo()
        {
            using (WebClient webClient = new WebClient())
            {
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

                return launcherInfo;
            }
        }

        public static Manifest GetManifest(String fileIndex)
        {
            using (WebClient webClient = new WebClient())
            {
                Manifest manifest = webClient.DownloadString(fileIndex).FromJSON<Manifest>();

                return manifest;
            }
        }

        public static void DownloadPatch(Manifest manifest, String destinationPath = null)
        {
            if (String.IsNullOrWhiteSpace(destinationPath)) destinationPath = Environment.CurrentDirectory;

            using (WebClient webClient = new WebClient())
            {
                Int32 i = 0;

                Parallel.ForEach(
                    Enumerable.Range(0, manifest.FileList.Length),
                    new ParallelOptions { MaxDegreeOfParallelism = MAX_THREADS },
                    j =>
                    {
                        String file = manifest.FileList[j];
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
            }
        }

        public static void UpdatePatcherState(Universe universe, String destinationPath = null)
        {
            FileInfo patcherState = new FileInfo(Path.Combine(destinationPath, @"..\..\Patcher\PatcherState"));

            if (!patcherState.Exists) Console.WriteLine("Unable to locate PatcherState - you will need to manually update the PatcherState file, or run a verify.");

            if (patcherState.Exists)
            {
                Console.WriteLine("Updating PatcherState to skip verify process.");

                try
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

                        patcherState.Delete();

                        File.AppendAllLines(patcherState.FullName, outLines);
                    }

                    Console.WriteLine("PatcherState update complete - you can now launch StarCitizen.");
                }
                catch (Exception)
                {
                    Console.WriteLine("Unable to update PatcherState - you will need to manually update the PatcherState file, or run a verify.");
                }
            }
        }

        public static void Run()
        {
            // if (false)
            // {
            //     var buildNo = 485232;
            // 
            //     universe.Version = $"2.6.0 - {buildNo} - PTU";
            //     universe.FileIndex = $"http://1.webseed.robertsspaceindustries.com/FileIndex/sc-alpha-2.6.0/{buildNo}.json";
            // }

            String destinationPath = Environment.CurrentDirectory;

            // destinationPath = @"O:\Games\StarCitizen\StarCitizen\Test";

            Console.Clear();

            DirectoryInfo directory = new DirectoryInfo(destinationPath);

            if (!directory.Exists) directory.Create();

            Console.WriteLine("Loading LauncherInfo.");

            var launcherInfo = Program.GetLauncherInfo();

            Console.WriteLine("LauncherInfo Loaded.");

            if (launcherInfo.Universes.ContainsKey(directory.Name))
            {
                var oldVerse = launcherInfo.Universes[directory.Name];
                var newVerse = launcherInfo.Universes[directory.Name];

                Console.Title = $"Current {oldVerse.Name} build is {oldVerse.Version}";

                if (newVerse.FileIndex == oldVerse.FileIndex)
                {
                    Console.WriteLine("Waiting for new build.");
                    Console.WriteLine();
                    Console.WriteLine("Press any key to force download of the current build...");
                }

                Console.WriteLine();

                var i = 0;

                var cursorTop = Console.CursorTop;

                while (newVerse.FileIndex == oldVerse.FileIndex && !Console.KeyAvailable)
                {
                    var points = String.Join("", Enumerable.Range(1, i % 4).Select(c => '.'));

                    Console.SetCursorPosition(0, cursorTop);
                    Console.WriteLine($"Waiting {points}   ");

                    Thread.Sleep(1000);

                    if (i++ % 10 == 0)
                    {
                        launcherInfo = Program.GetLauncherInfo();
                        newVerse = launcherInfo.Universes[directory.Name];
                    }
                }

                Console.SetCursorPosition(0, cursorTop);

                Console.Title = $"Current {newVerse.Name} build is {newVerse.Version}";

                Console.WriteLine($"{newVerse.Name} build {newVerse.Version} found - now downloading.");
                Console.WriteLine();

                var manifest = Program.GetManifest(newVerse.FileIndex);

                Program.DownloadPatch(manifest, destinationPath);

                Console.Title = $"Download of {newVerse.Name} complete.";

                Console.WriteLine();
                Console.WriteLine($"Download of {newVerse.Name} complete.");

                Console.WriteLine();

                Program.UpdatePatcherState(newVerse, destinationPath);
            }
                
            Thread.Sleep(10000);
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