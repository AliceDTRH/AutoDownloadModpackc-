using System;
using System.IO;
using System.Threading.Tasks;
using Polly.Bulkhead;
using Polly;

namespace ResilientDownloadLib
{
    public static class Logger
    {
        static readonly Random rnd = new Random();
        public const string BR = "\n";
        static object lastMessage = "";
        static LogType lastLogType;
        static int lastMessageamount;
        private static readonly AsyncBulkheadPolicy logQueue = Policy.BulkheadAsync(1, int.MaxValue);

        const string file = "./ADM.log";

        private static readonly Polly.Retry.AsyncRetryPolicy retryInfiniteNoLog = Policy.Handle<Exception>().WaitAndRetryForeverAsync(retryAttempt =>
        {
            return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(rnd.Next(0, 1000));
        });

        internal static StreamWriter Fs { get; set; }

        static async public Task Log(object message, LogType type = LogType.INFO) {
            await logQueue.ExecuteAsync(async () =>
            {
                if (lastMessage.Equals(message))
                {
                    lastMessageamount += 1;
                    return;
                }
                else {
                    if (lastMessageamount > 0) {
                        Log($"Ignoring {lastMessageamount} repeated messages. ({lastMessage})", lastLogType).FireAndForget();
                        lastMessageamount = 0;
                        
                    }
                }
                switch (message)
                {
                    case string _:
                        await _Log($"[{DateTime.Now}] ({type}) {(string)message}", type).ConfigureAwait(false);
                        break;
                    default:
                        await _Log($"[{DateTime.Now}] ({type}) {message}", type).ConfigureAwait(false);
                        break;
                }
                lastMessage = message;
                lastLogType = type;
            }).ConfigureAwait(false);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:Fire & forget async void methods", Justification = "We are using this to fire and forget with error handling.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "We are using this to fire and forget with error handling.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1030:Use events where appropriate", Justification = "We are using this to fire and forget with error handling.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Bug", "S3168:\"async\" methods should not return \"void\"", Justification = "We are using this to fire and forget with error handling.")]
        static async public void FireAndForget(this Task task) {
            try
            {
                if (task != null)
                {
                    await task.ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Error while writing to log: {e}").ConfigureAwait(false);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1305:Specify IFormatProvider", Justification = "Not important.")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Internal function")]
        static private async Task _Log(string msg, LogType type) {
            await retryInfiniteNoLog.ExecuteAsync(async () =>
            {
                if (Fs == null) { Fs = new StreamWriter(file, true); }

                if (type == LogType.FATAL) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    string dashes = "-----------------------------------------------------------------------------";
                    msg = dashes + BR + msg + BR + dashes;
                }
                if (type != LogType.DEBUG) { Console.WriteLine(msg); }
                
                await Fs.WriteLineAsync(msg.ToString()).ConfigureAwait(false);
                await Fs.FlushAsync().ConfigureAwait(false);
                
                Console.ResetColor();
            }).ConfigureAwait(false);
        }
    }



    public enum LogType
    {
        DEBUG,
        INFO,
        NOTICE,
        WARNING,
        ERROR,
        FATAL
    }
}
