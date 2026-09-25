using System;
using System.Text;

namespace 云湖WP.Utils
{
    /// <summary>
    /// 全局应用运行与网络上传日志记录器
    /// </summary>
    public static class AppLogger
    {
        private static readonly StringBuilder _logs = new StringBuilder();
        private static readonly object _lock = new object();

        public static void Log(string tag, string message)
        {
            string entry = string.Format("[{0:HH:mm:ss.fff}][{1}] {2}\r\n", DateTime.Now, tag, message);
            lock (_lock)
            {
                _logs.Append(entry);
                if (_logs.Length > 30000)
                {
                    _logs.Remove(0, 8000);
                }
            }
            System.Diagnostics.Debug.WriteLine(entry.TrimEnd());
        }

        public static string GetAllLogs()
        {
            lock (_lock)
            {
                return _logs.ToString();
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _logs.Clear();
            }
        }
    }
}
