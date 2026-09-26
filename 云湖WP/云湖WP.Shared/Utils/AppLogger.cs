using System;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 全局应用运行与崩溃持久化日志记录器 (支持内存与本地磁盘文件持久化存储)
    /// </summary>
    public static class AppLogger
    {
        private static readonly StringBuilder _logs = new StringBuilder();
        private static readonly object _lock = new object();
        private const string LogFileName = "app_diagnostic.log";
        private static bool _isWritingFile = false;
        private static readonly StringBuilder _pendingDiskLogs = new StringBuilder();

        public static void Log(string tag, string message)
        {
            string entry = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}][{1}] {2}\r\n", DateTime.Now, tag, message);
            lock (_lock)
            {
                _logs.Append(entry);
                if (_logs.Length > 40000)
                {
                    _logs.Remove(0, 10000);
                }
                _pendingDiskLogs.Append(entry);
            }
            System.Diagnostics.Debug.WriteLine(entry.TrimEnd());

            // 异步持久化写入本地磁盘文件，即使程序崩溃也能保留记录
            FlushToDiskAsync();
        }

        private static async void FlushToDiskAsync()
        {
            lock (_lock)
            {
                if (_isWritingFile || _pendingDiskLogs.Length == 0) return;
                _isWritingFile = true;
            }

            try
            {
                string textToWrite;
                lock (_lock)
                {
                    textToWrite = _pendingDiskLogs.ToString();
                    _pendingDiskLogs.Clear();
                }

                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.CreateFileAsync(LogFileName, CreationCollisionOption.OpenIfExists);
                await FileIO.AppendTextAsync(file, textToWrite);
            }
            catch { }
            finally
            {
                lock (_lock)
                {
                    _isWritingFile = false;
                }
            }
        }

        public static string GetAllLogs()
        {
            lock (_lock)
            {
                return _logs.ToString();
            }
        }

        public static async Task<string> ReadFullPersistedLogsAsync()
        {
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(LogFileName);
                if (file != null)
                {
                    return await FileIO.ReadTextAsync(file);
                }
            }
            catch { }
            return GetAllLogs();
        }

        public static async Task ClearAsync()
        {
            lock (_lock)
            {
                _logs.Clear();
                _pendingDiskLogs.Clear();
            }
            try
            {
                var folder = ApplicationData.Current.LocalFolder;
                var file = await folder.GetFileAsync(LogFileName);
                if (file != null)
                {
                    await file.DeleteAsync();
                }
            }
            catch { }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
                _pendingDiskLogs.Clear();
            }
            Task.Run(async () => await ClearAsync());
        }
    }
}
