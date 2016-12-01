using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace FastPatch
{
    class Program
    {
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

                String[] launcherInfo = webClient.DownloadString("http://manifest.robertsspaceindustries.com/Launcher/_LauncherInfo").Split(new Char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (String line in launcherInfo)
                {
                    if (line.StartsWith($"{directory.Name}_fileIndex = "))
                    {
                        Console.WriteLine($"{directory.Name} build found - now downloading");
                        Console.WriteLine();

                        Console.Title = $"Downloading latest {directory.Name} build";

                        String manifestPath = line.Split('=')[1].Trim();

                        Manifest manifest = webClient.DownloadString(manifestPath).FromJSON<Manifest>();

                        String[] fileList = manifest.FileList;

                        Enumerable.Range(0, fileList.Length).AsParallel().ForAll(j =>
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

                        Console.Title = $"Download of {directory.Name} complete";

                        Console.WriteLine();
                        Console.WriteLine($"Download of {directory.Name} complete");
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
}