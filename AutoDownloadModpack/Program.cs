using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Bson;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System.IO;
using System.Security.Cryptography;
using MonkeyCache;
using MonkeyCache.LiteDB;

namespace AutoDownloadModpack
{
    static class Program
    {
        // {63B564D5-B247-481A-9790-4BB8A3A4335A}
        public static readonly Guid guid = System.Guid.Parse("63B564D5-B247-481A-9790-4BB8A3A4335A");

        static readonly public HttpClient client = new HttpClient();
        static readonly string path = Path.GetFullPath("./profile/");

        static readonly public string host = "https://launcher.alicedtrh.xyz/";
        internal static readonly string database = Path.GetFullPath("./launcherdb/");
        internal static int runningDownloads;
        internal static Random rnd = new Random();

        static async Task Main(string[] args)
        {
            Barrel.ApplicationId = "alicedtrhadm";
            BarrelUtils.SetBaseCachePath("./launcherdata");

            await ConfirmGuid();

            var down = await DownloadTextFile(host + "generateModList.php?v=" + Properties.Settings.Default.Version);
            dynamic fileList = JObject.Parse(down) as dynamic;
            float version = fileList.Version;
            Console.WriteLine("Checking for mods to download.");
            if (fileList.AmountOfMods > 0)
            {
                List<Task> mods = new List<Task>();
                foreach (var mod in fileList.FileList)
                {
                    Mod thisMod = new Mod(path);
                    //Console.WriteLine(path + mod.path);
                    thisMod.Path = mod.path;
                    thisMod.Sha1 = mod.sha1;
                    if (mod.purge == Properties.Settings.Default.Version) {
                        thisMod.Purge = true;
                    }
                    Task PM;
                    if (mod.conf != null && mod.conf == true)
                    {
                        PM = ProcessMod(thisMod, true);
                    }
                    else {
                        PM = ProcessMod(thisMod);
                    }

                    mods.Add(PM);

                }
                await Task.WhenAll(mods);
                Console.WriteLine("Done!");
            
                
            }
            else
            {
                Console.WriteLine("No mods listed in file.");
            }
            if (version > Properties.Settings.Default.Version)
            {
                Properties.Settings.Default.Version = version;
                _ = Console.Out.WriteLineAsync("New modpack version found. Updating internal version counter.");
            }
            else
            {
                Console.Error.WriteLine("No new modpack version found.");
            }
            await Console.Out.FlushAsync();
            Console.ReadLine();
        }

        async private static Task ConfirmGuid()
        {
            try
            {
                Guid Rguid = Guid.Parse(await DownloadTextFile(host + "guid.txt", 3));
                if (!Rguid.Equals(Program.guid))
                {
                    Console.Error.WriteLine("You are using an outdated version of this downloader/launcher script. For more information, contact Alice.\nNo support is offered for this version. Press ENTER to run anyway.");
                    Console.ReadLine();
                }
            }
            catch (FormatException)
            {
                Console.WriteLine("Invalid GUID");
            }
            catch (System.ArgumentNullException)
            {
                Console.WriteLine("Unable to get GUID file.");
            }
        }

        private static async Task ProcessMod(Mod mod, bool conf = false)
        {
                string etag = Barrel.Current.Get<string>(key: mod.Path);
            if (File.Exists(mod.Path) && etag == null) {
                var sha1 = await GetHashAsync<SHA1Managed>(File.OpenRead(mod.Path));
                if (sha1 == mod.Sha1) {
                    DownloadManager downloadManager = new DownloadManager(mod);
                    await downloadManager.getETAG();
                    downloadManager.WriteEtag(mod);
                    etag = Barrel.Current.Get<string>(key: mod.Path);
                    Console.WriteLine(mod.Path + " was added through sha1 hash.");

                }
            }
                if (File.Exists(mod.Path) && etag != null)
                {
                    if (mod.Purge == true)
                    {
                        File.Delete(mod.Path);
                    }

                    await DownloadMod(mod, etag, conf);
                }
                else
                {
                    await DownloadMod(mod, conf: conf);
                }
                await Task.Delay(250);
            
        }

        private static async Task DownloadMod(Mod mod, string ETAG = null, bool conf = false)
        {
            if (ETAG != null)
            {
                var headerRequest = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, mod.DownloadUrl));
                if (headerRequest.Headers.ETag.ToString() == ETAG) { return; }
            }
            DownloadManager dm = new DownloadManager(mod);
            if (conf == false)
            {
                await dm.Download();
            }
            else {
                await dm.DownloadConf();
            }
            

        }

        public static async Task<string> GetHashAsync<T>(this Stream stream)
    where T : HashAlgorithm, new()
        {
            StringBuilder sb;

            using (var algo = new T())
            {
                var buffer = new byte[8192];
                int bytesRead;

                // compute the hash on 8KiB blocks
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    algo.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                algo.TransformFinalBlock(buffer, 0, bytesRead);

                // build the hash string
                sb = new StringBuilder(algo.HashSize / 4);
                foreach (var b in algo.Hash)
                    sb.AppendFormat("{0:x2}", b);
            }

            return sb?.ToString();
        }

        private async static Task<string> DownloadTextFile(string v, int tries = 15)
        {
            for (int a = 0; a < tries; a++)
            {
                try
                {
                    string responseBody = await client.GetStringAsync(v);
                    return responseBody;
                }
                catch (System.Net.Http.HttpRequestException e)
                {
                    _ = Console.Error.WriteLineAsync(string.Format("Error downloading {0}: {1}", v, e.Message));
                    await Task.Delay(1000);
                }
            }
            return null;
        }
    }
}
