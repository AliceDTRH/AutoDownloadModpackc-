using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CSHash.Digests;
using ResilientDownloadLib;
using LightningBug.Polly;
using Newtonsoft.Json;
using Downloader;
using System.Net;
using System.Reflection;
using System.Diagnostics;

namespace AutoDownloadModpack
{
    class Program
    {
        private const string LocalPath = "./profile";
        private const string host = "https://alicedtrh.xyz/";
        private static int finished;
        private static int publiccount = 1000;

        static  DownloadService downloader = new DownloadService(downloadOpt);
        private static Dictionary<string, Uri> origRemoteFileList = new Dictionary<string, Uri>();
        static readonly DownloadConfiguration downloadOpt = new DownloadConfiguration()
        {
            MaxTryAgainOnFailover = int.MaxValue, // the maximum number of times to fail.
            ParallelDownload = true, // download parts of file as parallel or not default value is false
            ChunkCount = 4, // file parts to download, default value is 1
            Timeout = 1000, // timeout (millisecond) per stream block reader, default valuse is 1000
            OnTheFlyDownload = false, // caching in-memory or not? default valuse is true
            RequestConfiguration = // config and customize request headers
    {
        Accept = "*/*",
        UserAgent = $"ResilientDownloadLib/{Assembly.GetExecutingAssembly().GetName().Version.ToString(3)}",
        ProtocolVersion = HttpVersion.Version11,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        KeepAlive = false,
        UseDefaultCredentials = false
    }
        };

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2008:Do not create tasks without passing a TaskScheduler", Justification = "<Pending>")]
        public async static Task Main()
        {
            Modpack modpack = new Modpack();

            Dictionary<string, string> remoteDict = new Dictionary<string, string>();
            Dictionary<string, string> localDict = new Dictionary<string, string>();

            Dictionary<string, string> diffDict = new Dictionary<string, string>();

            Task<Dictionary<string, string>> remoteTask = Task.Run(GetRemoteFilelistFromServer);

            List<Task> tasks = new List<Task>();

            Task<Dictionary<string, string>> localTask = Task.Run(GenerateLocalFileList);
            tasks.Add(remoteTask.ContinueWith(async (t) => { remoteDict = await t.ConfigureAwait(false); }));
            tasks.Add(localTask.ContinueWith(async (t) => { localDict = await t.ConfigureAwait(false); }));
            
            while (finished < publiccount)
            {
                
                Logger.Log(finished + "/" + publiccount).FireAndForget();
                await Task.Delay(100).ConfigureAwait(false);
            }
            Logger.Log(finished + "/" + publiccount).FireAndForget();

            await Task.WhenAll(tasks.ToArray()).ConfigureAwait(false);

            List<Task> diffTasks = new List<Task>();

            foreach (var item in remoteDict)
            {
                diffTasks.Add(Task.Run(() =>
                 {
                     KeyValuePair<string, string> newadd = ProcessDifferences(item, localDict);
                     if (newadd.Key != null && newadd.Value != null)
                     {
                         diffDict.Add(newadd.Key, newadd.Value);
                     }
                 }));
            }

            await Task.WhenAll(diffTasks.ToArray()).ConfigureAwait(false);

            List<Task> downloadTasks = new List<Task>();

            foreach (var item in diffDict)
            {
                if (item.Value == "da39a3ee5e6b4b0d3255bfef95601890afd80709") { Logger.Log("Empty file..").FireAndForget(); }
                var uri = origRemoteFileList[item.Key];
                try
                {
                    await downloader.DownloadFileAsync(uri.ToString(), item.Key).ConfigureAwait(false);
                }
                catch (System.Net.WebException e)
                {
                    if (e.Message == "The remote server returned an error: (404) Not Found.")
                    {
                        Logger.Log("Could not download " + uri + ": 404 not found.", LogType.ERROR).FireAndForget();
                    }
                    else { throw; }
                    
                }
                


            }
            

            


            //Stopping the closing
            Logger.Log("Application done").FireAndForget();
            Console.ReadKey();
        }

        private static KeyValuePair<string, string> ProcessDifferences(KeyValuePair<string, string> item, Dictionary<string, string> localDict)
        {
            if (!localDict.ContainsKey(item.Key))
            {
                Logger.Log($"{item.Key} not found in localDict", LogType.DEBUG).FireAndForget();
                return item;
            }

            if (localDict.TryGetValue(item.Key, out string value) == false)
            {
                Logger.Log($"Couldn't get value for {item.Key} in localDict.", LogType.DEBUG).FireAndForget();

                return item;
            }

            if (NormalizePath(item.Value) != NormalizePath(value))
            {
                Logger.Log($"{NormalizePath(item.Value)} != {NormalizePath(value)}", LogType.DEBUG).FireAndForget();

                return item;
            }

            return default;
        }

        private static Dictionary<string, string> GenerateLocalFileList()
        {
            Dictionary<string, string> results = new Dictionary<string, string>();
            Logger.Log("Searching for local files.").FireAndForget();
            
            try
            {
                IEnumerable<string> count = Directory.EnumerateFiles(LocalPath, "*.*", SearchOption.AllDirectories);
                publiccount = count.Count();
                Parallel.ForEach(count, (result) =>
                {
                    
                    string path = result.Remove(0, 9); //Remove ./profile from start of path.

                    SHA1 sha1 = new SHA1();
                    CSHash.Tools.Converter converter = new CSHash.Tools.Converter();

                    byte[] hash = sha1.HashFromFile(result);

                    string fullhash = converter.ConvertByteArrayToFullString(hash);

                    //Logger.Log($"{fullhash} - {NormalizePath(result)}").FireAndForget();

                    results.Add(NormalizePath(result), fullhash);
                    
                    finished++;
                });
            }
            catch (System.IO.DirectoryNotFoundException)
            {
                Logger.Log("Directory didn't exist.", LogType.DEBUG).FireAndForget();
                Directory.CreateDirectory(LocalPath);
                

            }

            finished = publiccount;
            Logger.Log("Returning " + results.Count + " local files.").FireAndForget();
            return results;
        }


        [LightningBug.Polly.Retry.Retry(15)]
        
        private static async Task<Dictionary<string, string>> GetRemoteFilelistFromServer()
        {
            Logger.Log("Getting remote file list from server.").FireAndForget();
            using var client = new HttpClient();
            var request = await client.GetAsync(new Uri(host + "filelist.json")).ConfigureAwait(false);

            string result = await request.Content.ReadAsStringAsync().ConfigureAwait(false);

            Dictionary<string, string> newFileList = new Dictionary<string, string>();
            Logger.Log("Converting JSON to file list.").FireAndForget();
            foreach (var item in JsonConvert.DeserializeObject<RemoteFileList>(result).fileList)
            {
                newFileList.Add(NormalizePath(item.Key), item.Value);
                origRemoteFileList.Add(NormalizePath(item.Key), new Uri(host + item.Key.Remove(0, 10)));
            }


            Logger.Log("Returning remote files.").FireAndForget();
            return newFileList;
        }

        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(path);
        }

    }



}
