using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using System;
using System.Collections.Generic;
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
        public static void WriteLine(string message, int level)
        {
            level = level >= 1 ? level : 1;

            Console.WriteLine($"{new string(' ', (level - 1) * 3)}{message}");
        }
    }
}
