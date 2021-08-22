using Microsoft.Azure.Storage.Core.Util;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReplicateAMSv3
{
    public class UploadProgress : IProgress<StorageProgress>
    {
        private long _total = 0;
        private int _lastProgress = -1;

        public UploadProgress(long total)
        {
            _total = total;
        }

        public void Report(StorageProgress value)
        {
            int tempProgress = (Int32)(Convert.ToDecimal(value.BytesTransferred) / Convert.ToDecimal(_total) * 100);

            if (tempProgress % 10 == 0 && tempProgress > _lastProgress)
            {
                _lastProgress = tempProgress;
                switch (_lastProgress)
                {
                    case 0:
                        Helpers.Write($"{_lastProgress}%", 1);
                        break;
                    case 100:
                        Helpers.WriteLine($" --> {_lastProgress}%", 1);
                        break;
                    default:
                        Helpers.Write($" --> {_lastProgress}%", 1);
                        break;
                }
            }
        }
    }
}
