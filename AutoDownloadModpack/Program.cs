using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using CSHash.Digests;
using ResilientDownloadLib;
using Newtonsoft.Json;
using Downloader;
using System.Net;
using System.Reflection;
using Polly;

namespace AutoDownloadModpack
{
    internal static class Program
    {
        private const string host = "https://alicedtrh.xyz/";
        private const string filelist = "filelist.php";
        private static readonly string location = System.Reflection.Assembly.GetExecutingAssembly().Location;

        private static readonly AsyncPolicy RetryPolicy = Policy.Handle<Exception>((a) =>
        {
            if (a.Message == "File size is invalid!")
            {
                Logger.Log("Error during download: " + a.Message).FireAndForget(); return false; //Workaround might no longer be needed due to c54d8c6
            }
            else { Logger.Log("Error during download: " + a.Message).FireAndForget(); return true; }
        }).WaitAndRetryAsync(10, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

        private static Dictionary<string, Uri> origRemoteFileList = new Dictionary<string, Uri>();

        private static readonly DownloadConfiguration downloadOpt = new DownloadConfiguration()
        {
            MaxTryAgainOnFailover = int.MaxValue, // the maximum number of times to fail.
            ParallelDownload = true, // download parts of file as parallel or not default value is false
            ChunkCount = 4, // file parts to download, default value is 1
            Timeout = 1000, // timeout (millisecond) per stream block reader, default valuse is 1000
            OnTheFlyDownload = false, // caching in-memory or not? default valuse is true
            RequestConfiguration = // config and customize request headers
    {
        Accept = "*/*",
        UserAgent = Version(),
        ProtocolVersion = HttpVersion.Version11,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        KeepAlive = false,
        UseDefaultCredentials = false
    }
        };

        private static string Version()
        {
            return $"{Assembly.GetExecutingAssembly().GetName().Name}/{Assembly.GetExecutingAssembly().GetName().Version}";
        }

        private static readonly DownloadConfiguration downloadOptSmall = new DownloadConfiguration()
        {
            MaxTryAgainOnFailover = int.MaxValue, // the maximum number of times to fail.
            ParallelDownload = false, // download parts of file as parallel or not default value is false
            ChunkCount = 1, // file parts to download, default value is 1
            Timeout = 1000, // timeout (millisecond) per stream block reader, default valuse is 1000
            OnTheFlyDownload = true, // caching in-memory or not? default valuse is true
            RequestConfiguration = // config and customize request headers
    {
        Accept = "*/*",
        UserAgent = Version(),
        ProtocolVersion = HttpVersion.Version11,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        KeepAlive = false,
        UseDefaultCredentials = false
    }
        };

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", Justification = "<Pending>")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "<Pending>")]
        public static async Task Main()
        {
            FileStream lockFile = File.OpenWrite("lock");
            CreateProfileDirectories();

            ushort retries = 0;
            while (await Run() > 0)
            {
                ++retries;
                if (retries > 3) { await Logger.Log("Too many retries, contact Alice for support!", LogType.FATAL); break; }
                origRemoteFileList = new Dictionary<string, Uri>();
                Logger.Log("Restarting application to check state.").FireAndForget();
            }
            //Stopping the closing

            if (retries < 3)
            {
                Logger.Log("All files were verified.").FireAndForget();
                if (!Console.IsOutputRedirected)
                {
                    Console.ReadKey();
                }
            }
            else
            {
                if (!Console.IsOutputRedirected) { Console.ReadKey(); }
            }

            lockFile.Close();
        }

        private static void CreateProfileDirectories()
        {
            if (!Directory.Exists("./mods") && IsInRoot("./mods"))
            {
                Directory.CreateDirectory("./mods");
            }
            if (!Directory.Exists("./config") && IsInRoot("./config"))
            {
                Directory.CreateDirectory("./config");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2008:Do not create tasks without passing a TaskScheduler", Justification = "<Pending>")]
        private static async Task<int> Run()
        {
            Dictionary<string, string> remoteDict;
            Dictionary<string, string> localDict = new Dictionary<string, string>();

            Dictionary<string, string> diffDict = new Dictionary<string, string>();

            Task<Dictionary<string, string>> remoteTask = Task.Run(GetRemoteFilelistFromServer);

            List<Task> tasks = new List<Task>();

            localDict = await remoteTask.ContinueWith((t) => { return GenerateLocalFileList(t.Result); });

            remoteDict = await remoteTask;

            await Task.WhenAll(tasks.ToArray());

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

            await Task.WhenAll(diffTasks.ToArray());

            localDict.Clear();
            remoteDict.Clear();

            Parallel.ForEach(diffDict, new ParallelOptions { MaxDegreeOfParallelism = 4 }, (KeyValuePair<string, string> item) =>
            {
                if (item.Value == "da39a3ee5e6b4b0d3255bfef95601890afd80709") { Logger.Log($"Not downloading empty file {item.Key}", LogType.DEBUG).FireAndForget(); File.Create(item.Key).Dispose(); return; }
                var uri = origRemoteFileList[item.Key];

#pragma warning disable AsyncFixer02 // Long-running or blocking operations inside an async method
                DownloadItem(item, uri).Wait(); //Only works using Wait()
#pragma warning restore AsyncFixer02 // Long-running or blocking operations inside an async method
            });

            return diffDict.Count;
        }

        private static async Task DownloadItem(KeyValuePair<string, string> item, Uri uri)
        {
            try
            {
                Logger.Log($"Starting download for {uri}").FireAndForget();

                bool isSmall = false;
                using (var client = new HttpClient())
                {
                    HttpRequestMessage m = new HttpRequestMessage(HttpMethod.Head, uri);

                    HttpResponseMessage resp = await client.SendAsync(m);

                    if (resp.Content.Headers.ContentLength < 100) { isSmall = true; }

                    if (resp.Content.Headers.ContentType.ToString() == "text/plain" || resp.Content.Headers.ContentType.ToString() == "text/json")
                    {
                        isSmall = true;
                    }

                    m.Dispose();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(NormalizePath(item.Key)));
                try
                {
                    if (isSmall)
                    {
                        DownloadService ds = new DownloadService(downloadOptSmall);
                        await RetryPolicy.ExecuteAsync(async () => { await ds.DownloadFileAsync(uri.ToString(), item.Key); });
                    }
                    else
                    {
                        await RetryPolicy.ExecuteAsync(async () => { await new DownloadService(downloadOpt).DownloadFileAsync(uri.ToString(), item.Key); });
                    }
                }
                catch (System.IO.InvalidDataException)
                {
                    await RetryPolicy.ExecuteAsync(async () => { await DownloadManually(uri, item.Key); });
                }

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

        private static async Task DownloadManually(Uri uri, string path)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", Version());
            var request = await client.GetAsync(uri, HttpCompletionOption.ResponseContentRead);
            request.EnsureSuccessStatusCode();
            File.WriteAllBytes(path, await request.Content.ReadAsByteArrayAsync());
        }

        private static KeyValuePair<string, string> ProcessDifferences(KeyValuePair<string, string> item, Dictionary<string, string> localDict)
        {
            if (!localDict.ContainsKey(item.Key))
            {
                Logger.Log($"{item.Key} not found in localDict", LogType.DEBUG).FireAndForget();
                return item;
            }

            if (!localDict.TryGetValue(item.Key, out string value))
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

            Parallel.ForEach(remoteFileList, (item) =>
            {
                if (File.Exists(NormalizePath(item.Key)))
                {
                    SHA1 sha1 = new SHA1();
                    CSHash.Tools.Converter converter = new CSHash.Tools.Converter();

                    byte[] hash = sha1.HashFromFile(item.Key);

                    string fullhash = converter.ConvertByteArrayToFullString(hash);

                    results.Add(NormalizePath(item.Key), fullhash);
                }
            });

            Logger.Log("Returning " + results.Count + " local files.").FireAndForget();
            Logger.Log(results, LogType.DEBUG).FireAndForget();
            return results;
        }

        private static async Task<Dictionary<string, string>> GetRemoteFilelistFromServer()
        {
            Logger.Log("Getting remote file list from server.").FireAndForget();
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", Version());
            var request = await client.GetAsync(new Uri(host + filelist));

            string result = await request.Content.ReadAsStringAsync();

            Dictionary<string, string> newFileList = new Dictionary<string, string>();
            Logger.Log("Converting JSON to file list.").FireAndForget();
            Logger.Log(result, LogType.DEBUG).FireAndForget();
            RemoteFileList remoteFileList = JsonConvert.DeserializeObject<RemoteFileList>(result);
            Logger.Log(remoteFileList, LogType.DEBUG).FireAndForget();
            foreach (var item in remoteFileList.fileList)
            {
                if (!IsInRoot(item.Key)) { Logger.Log($"Path not in root: {item}. Not downloading file.", LogType.ERROR).FireAndForget(); continue; }
                newFileList.Add(NormalizePath(item.Key), item.Value);
                origRemoteFileList.Add(NormalizePath(item.Key), new Uri(host + item.Key));
            }

            foreach (var item in remoteFileList.deleteFileList)
            {
                DeleteLocalFile(item);
            }

            Logger.Log("Returning remote files.").FireAndForget();
            return newFileList;
        }

        private static void DeleteLocalFile(string item)
        {
            string file = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(location), item.ToString()));

            if (!IsInRoot(file))
            {
                Logger.Log($"Attempt to delete file {file} which is outside of root! Ignoring.", LogType.WARNING).FireAndForget();
                return;
            }

            if (File.Exists(file))
            {
                Logger.Log("Deleting " + file).FireAndForget();
                File.Delete(item);
            }
        }

        private static bool IsInRoot(string file)
        {
            return Path.GetFullPath(file).StartsWith(Path.GetDirectoryName(location), StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(path);
        }
    }
}