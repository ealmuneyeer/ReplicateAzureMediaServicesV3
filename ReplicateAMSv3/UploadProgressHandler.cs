//using Microsoft.Azure.Storage.Core.Util;
using System;
using System.Collections.Generic;
using System.Text;

namespace ReplicateAMSv3
{
    class UploadProgressHandler : IProgress<long>
    {
        private long _total = 0;
        private int _lastProgress = -1;

        public UploadProgressHandler(long total)
        {
            _total = total;
        }

        public void Report(long value)
        {
            int tempProgress = (Int32)(value / Convert.ToDecimal(_total) * 100);

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
