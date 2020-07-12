using System;
using System.IO;
using System.ComponentModel;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Downloader;
using System.Net.Http;
using MonkeyCache.FileStore;


namespace AutoDownloadModpack
{
    internal class DownloadManager
    {
        private Mod mod;
        private string ETAG;

        DownloadConfiguration downloadOpt = new DownloadConfiguration()
        {
            MaxTryAgainOnFailover = int.MaxValue, // the maximum number of times to fail.
            ParallelDownload = true, // download parts of file as parallel or notm default value is false
            ChunkCount = 1, // file parts to download, default value is 1
            Timeout = 1000, // timeout (millisecond) per stream block reader, default valuse is 1000
            OnTheFlyDownload = false, // caching in-memory or not? default valuse is true
            //MaximumBytesPerSecond = (1024 * 1024) * 2, // download speed limited to 1MB/s, default valuse is zero or unlimited
            RequestConfiguration = {
               AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
               KeepAlive = true
            }
            
        };

        public DownloadManager(Mod mod)
        {
            this.mod = mod;
            
        }

        async public Task Download()
        {
                while (Program.runningDownloads > 150)
                {
                await Task.Delay(5000);
                }
            Program.runningDownloads++;
            int total = Program.runningDownloads;
            await Console.Out.WriteLineAsync("Downloading ("+ total +"/8 active downloads) "+mod.DownloadUrl+".");
            Console.Out.WriteLine("a");
            await getETAG();
            Console.Out.WriteLine("a");
            var downloader = new DownloadService(downloadOpt);
            downloader.DownloadFileCompleted += OnDownloadFileCompleted;
            downloader.DownloadProgressChanged += OnDownloadProgressChanged;
            try
            {
                await downloader.DownloadFileAsync(mod.DownloadUrl, mod.Path);
                
            }
            catch (InvalidDataException e) {
                await Console.Error.WriteLineAsync("ERROR: "+e.Message);

            }
            downloader.Clear();
        }

        private void OnDownloadProgressChanged(object sender, Downloader.DownloadProgressChangedEventArgs e)
        {
            
            Console.Title = this.mod.Path + ": " + e.ProgressPercentage;
        }

        internal async Task getETAG()
        {
            while (this.ETAG == null)
            {
                await Console.Out.FlushAsync();
                try
                {
                    Console.WriteLine(mod.ToString());
                    using (HttpResponseMessage headerRequest = await Program.client.SendAsync(new HttpRequestMessage(HttpMethod.Head, mod.DownloadUrl)))
                    {
                        _ =Console.Out.WriteLineAsync("Looking up identifier for file: "+ mod.DownloadUrl);
                        if (!string.IsNullOrEmpty(headerRequest.Headers.ETag.ToString()))
                        {
                            ETAG = headerRequest.Headers.ETag.ToString();
                            break;
                        }
                        else
                        {
                            await Console.Out.WriteLineAsync("Etag not found, retrying.");
                            await Task.Delay(500);
                        }
                    }
                }
                catch (InvalidDataException e) {
                    _ = Console.Out.WriteLineAsync(e.Message);
                }

            }
        }

        private void OnDownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            Program.runningDownloads--;
            if(e.Cancelled) { return; }
            if (e.Error != null) {
                Console.Error.WriteLine("Something went wrong ("+e.Error.Message+") downloading "+mod.DownloadUrl);
                return;
            }
            WriteEtag(mod);
            Console.Out.WriteLineAsync("Download completed: " +mod.DownloadUrl);

            
        }

        internal void WriteEtag(Mod mod)
        {
            Barrel.Current.Add(key: mod.Path, data: this.ETAG, expireIn: TimeSpan.FromDays(365));
        }


        internal async Task DownloadConf()
        {
            Task etaggetter = getETAG();
            Console.WriteLine("Downloading "+ mod.Path);
            Uri.TryCreate(mod.DownloadUrl, UriKind.Absolute, out Uri url);
            WebClient client = new WebClient();
            Stream data = await client.OpenReadTaskAsync(url);
            FileStream fileStream = new FileStream(mod.Path, FileMode.CreateNew);
            await data.CopyToAsync(fileStream);
            data.Dispose();
            fileStream.Dispose();
            etaggetter.Wait();
            WriteEtag(mod);
            Console.WriteLine("Done downloading "+fileStream.Name+ " " + fileStream.Length);
        }
    }
}