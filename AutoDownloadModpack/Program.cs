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
using Polly;

namespace AutoDownloadModpack
{
    class Program
    {
        
        private const string host = "https://alicedtrh.xyz/";


        readonly static string location = System.Reflection.Assembly.GetExecutingAssembly().Location;
        //Just triggering codefactor


        private static readonly AsyncPolicy RetryPolicy = Policy.Handle<Exception>().WaitAndRetryForeverAsync(retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), (e, _) => {
            Logger.Log($"{e.GetType().Name}: {e.Message} - Retrying", LogType.INFO).FireAndForget();
            });

        static readonly DownloadService downloader = new DownloadService(downloadOpt);
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

        
        public static async Task Main()
        {
            ushort retries = 0;
            while (await Run().ConfigureAwait(false) > 0)
            {
                ++retries;
                if (retries > 3) { await Logger.Log("Too many retries, contact Alice for support!", LogType.FATAL).ConfigureAwait(false); break;   }
                origRemoteFileList = new Dictionary<string, Uri>();
                Logger.Log("Restarting application to check state.").FireAndForget();
            }
            //Stopping the closing

            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (retries < 3)
            {
                Logger.Log("All files were verified.").FireAndForget();
                Console.ReadKey();
            }
            else {
                Console.ReadKey();
            }


        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2008:Do not create tasks without passing a TaskScheduler", Justification = "<Pending>")]
        private static async Task<int> Run()
        {
            {
                Modpack modpack = new Modpack();

                Dictionary<string, string> remoteDict = new Dictionary<string, string>();
                Dictionary<string, string> localDict = new Dictionary<string, string>();

                Dictionary<string, string> diffDict = new Dictionary<string, string>();

                Task<Dictionary<string, string>> remoteTask = Task.Run(GetRemoteFilelistFromServer);

                List<Task> tasks = new List<Task>();

                localDict = await remoteTask.ContinueWith((t) => { return GenerateLocalFileList(t.Result); }).ConfigureAwait(false);

                remoteDict = await remoteTask.ConfigureAwait(false);



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

                foreach (KeyValuePair<string, string> item in diffDict)
                {
                    if (item.Value == "da39a3ee5e6b4b0d3255bfef95601890afd80709") { File.Create(item.Key).Dispose(); continue; }
                    var uri = origRemoteFileList[item.Key];
                    try
                    {
                        Logger.Log($"Starting download for {uri}").FireAndForget();

                        await RetryPolicy.ExecuteAsync(async () => { await downloader.DownloadFileAsync(uri.ToString(), item.Key).ConfigureAwait(false); }).ConfigureAwait(false);
                        Logger.Log($"Download for {uri} finished.").FireAndForget();
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

                return diffDict.Count;


            }
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

        private static Dictionary<string, string> GenerateLocalFileList(Dictionary<string, string> remoteFileList)
        {
            Dictionary<string, string> results = new Dictionary<string, string>();
            Logger.Log("Searching for local files.").FireAndForget();

            Parallel.ForEach(remoteFileList, (item) => {
                if (File.Exists(NormalizePath(item.Key))) {
                    SHA1 sha1 = new SHA1();
                    CSHash.Tools.Converter converter = new CSHash.Tools.Converter();

                    byte[] hash = sha1.HashFromFile(item.Key);

                    string fullhash = converter.ConvertByteArrayToFullString(hash);

                    results.Add(NormalizePath(item.Key), fullhash);
                }
            });

            
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
            RemoteFileList remoteFileList = JsonConvert.DeserializeObject<RemoteFileList>(result);
            foreach (var item in remoteFileList.fileList)
            {
                if (!IsInRoot(item.Key)) { Logger.Log($"Path not in root: {item}. Not downloading file.", LogType.ERROR).FireAndForget(); continue; }
                newFileList.Add(NormalizePath(item.Key), item.Value);
                origRemoteFileList.Add(NormalizePath(item.Key), new Uri(host + item.Key.Remove(0, 10)));
            }

            foreach (var item in remoteFileList.deleteFileList) {
                DeleteLocalFile(item);
            }

            


            Logger.Log("Returning remote files.").FireAndForget();
            return newFileList;
        }

        private static void DeleteLocalFile(string item)
        {
            string file = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(location), item.ToString()));

            if (!IsInRoot(file)) {
                Logger.Log($"Attempt to delete file {file} which is outside of root! Ignoring.", LogType.WARNING).FireAndForget();
                return;
            }

            if (File.Exists(file) && !IsSymbolic(file))
            {
                Logger.Log("Deleting " + file).FireAndForget();
                File.Delete(item);
            }
        }

        private static bool IsInRoot(string file)
        {
            return Path.GetFullPath(file).StartsWith(Path.GetDirectoryName(location), StringComparison.OrdinalIgnoreCase);
        }


        //Technically catches any file with a reparsepoint. Good enough for our purposes.
        private static bool IsSymbolic(string path)
        {
            FileInfo pathInfo = new FileInfo(path);
            return pathInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }



        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(path);
        }

    }



}
