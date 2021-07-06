using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ReplicateAMSv3
{
    public static class Extensions
    {
        public enum BlobType
        {
            Unknown,
            BlockBlock,
            Directory
        }

        public static BlobType GetBlobType(this IListBlobItem blobItem)
        {
            if (blobItem.GetType() == typeof(CloudBlobDirectory))
            {
                return BlobType.Directory;
            }
            else if (blobItem.GetType() == typeof(CloudBlockBlob))
            {
                return BlobType.BlockBlock;
            }
            else
            {
                return BlobType.Unknown;
            }
        }
    }

    public static class Helpers
    {
        private static string logFile;

        public static void WriteLine(string message, int level)
        {
            level = level >= 1 ? level : 1;

            string msg = $"{new string(' ', (level - 1) * 3)}{message}";
            Console.WriteLine(msg);

            try
            {
                File.AppendAllText(logFile, msg + Environment.NewLine);
            }
            catch (Exception ex)
            {
                //do nothing
            }
        }

        public static void CreateLogFile()
        {
            logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", DateTime.Now.ToString("yyyyMMddHHmmss") + ".txt");

            if (!Directory.Exists(Path.GetDirectoryName(logFile)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFile));
            }

            using (File.Create(logFile)) { }
        }
    }
}
