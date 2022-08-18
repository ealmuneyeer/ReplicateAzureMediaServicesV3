using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ReplicateAMSv3.Managers
{
    public class AssetManager : ManagerBase<IAssetsOperations>
    {
        private IAssetFiltersOperations _sourceAssetFilterOperations;
        private IAssetFiltersOperations _destinationAssetFilterOperations;

        //0: AzCopy path/command
        //1: Source storage account name
        //2: Source container name
        //3: Source container SAS
        //4: Destination storage account name
        //5: Destination storage container name
        //6: Destination container SAS
        //7: Preserve access tier
        private const string AZ_COPY_COMMAND = "\"{0}\" copy \"{1}\" \"{2}\" --recursive --overwrite=ifSourceNewer --s2s-preserve-access-tier={3}";

        public void Initialize(IAssetsOperations sourceOperations, IAssetsOperations destinationOperations, ServicePrincipalAuth sourceAuth, ServicePrincipalAuth destinationAuth, Miscellaneous miscellaneous, IAssetFiltersOperations sourceAssetFiltersOperations, IAssetFiltersOperations destinationAssetFiltersOperations)
        {
            Initialize(sourceOperations, destinationOperations, sourceAuth, destinationAuth, miscellaneous);

            _sourceAssetFilterOperations = sourceAssetFiltersOperations;
            _destinationAssetFilterOperations = destinationAssetFiltersOperations;
        }

        public override bool Replicate()
        {
            //Loop through all the assets pages in the source and move them to the destination
            var assetPage = SourceOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName);
            ReplicateAssetPage(assetPage);

            while (assetPage.NextPageLink != null)
            {
                assetPage = SourceOperations.ListNext(assetPage.NextPageLink);
                ReplicateAssetPage(assetPage);
            }

            return true;
        }

        private void ReplicateAssetPage(IPage<Asset> assetPage)
        {
            if (assetPage.Any())
            {
                //Loop through page assets and move them to the destination
                foreach (var asset in assetPage)
                {
                    Helpers.WriteLine($"Replicating asset '{asset.Name}'...", 2);

                    Asset destinationAsset = DestinationOperations.CreateOrUpdate(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, asset.Name, new Asset() { AlternateId = asset.AlternateId, Description = asset.Description });

                    //Used to update asset info. sometimes it will be empty
                    destinationAsset = DestinationOperations.Get(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, destinationAsset.Name);

                    BlobContainerClient sourceContainer = GetBlobContainerClient(SourceAuth, asset.Container);
                    BlobContainerClient destinationContainer = GetBlobContainerClient(DestinationAuth, destinationAsset.Container);

                    CopyAsset(sourceContainer, destinationContainer);

                    if (_sourceAssetFilterOperations != null && _destinationAssetFilterOperations != null)
                    {
                        Helpers.WriteLine($"Copying asset's filters...", 3);
                        ReplicateAssetFilter(asset.Name);
                    }
                }
            }
            else
            {
                Helpers.WriteLine("No assets to copy", 2);
            }
        }

        private BlobContainerClient GetBlobContainerClient(ServicePrincipalAuth servicePrincipalAuth, string containerName)
        {
            return new BlobContainerClient(new Uri(servicePrincipalAuth.StorageAccountUrl.ToString() + containerName), new StorageSharedKeyCredential(servicePrincipalAuth.StorageAccountName, servicePrincipalAuth.StorageAccountKey));
        }

        private void CopyAsset(BlobContainerClient sourceContainer, BlobContainerClient destinationContainer)
        {
            List<BlobItem> sourceBlobs = new List<BlobItem>();
            List<BlobItem> destBlobs = new List<BlobItem>();

            sourceBlobs.AddRange(sourceContainer.GetBlobs().ToList());

            Helpers.WriteLine($"Copying asset's {sourceBlobs.Count.ToString("N0")} blob(s)...", 3);

            if (sourceBlobs.Any())
            {
                //Fill blobs in the destination
                destBlobs.AddRange(destinationContainer.GetBlobs().ToList());

                //Get blobs that exists in destination
                var intersect = sourceBlobs.Select(i => i.Name).Intersect(destBlobs.Select(i => i.Name)).ToList();

                long totalCount = sourceBlobs.Count;
                long copiedCount = intersect.Count;
                string existingBlobsMsg = $"Existing blobs: {intersect.Count.ToString("N0")}/{sourceBlobs.Count.ToString("N0")}";

                if (intersect.Any())
                {
                    intersect.ForEach(item =>
                    {
                        sourceBlobs.Remove(sourceBlobs.First(i => i.Name == item));
                    });
                }

                //Check if there are any remaining blobs in the source to copy
                if (sourceBlobs.Any())
                {
                    Helpers.WriteLine($"{existingBlobsMsg} --> Copying {sourceBlobs.Count.ToString("N0")} blob(s)...", 4);

                    if (Miscellaneous.UploadMedium.Equals(AppSettings.UploadMedium.AZ_COPY, StringComparison.InvariantCultureIgnoreCase))
                    {
                        CopyAssetAzCopy(sourceContainer, destinationContainer);
                    }
                    else
                    {
                        foreach (var sourceBlobItem in sourceBlobs)
                        {
                            copiedCount++;
                            CopyBlockBlob(sourceBlobItem.Name, sourceContainer, destinationContainer, $"{copiedCount.ToString("N0")}/{totalCount.ToString("N0")}");
                        }
                    }
                }
                else
                {
                    Helpers.WriteLine($"{existingBlobsMsg} --> No need to copy", 4);
                }
            }
            else
            {
                Helpers.WriteLine($"No blobs to copy", 4);
            }
        }

        private void CopyBlockBlob(string blobName, BlobContainerClient sourceBlobContainer, BlobContainerClient destinationBlobContainer, string progress)
        {
            BlobClient sourceBlobClient = sourceBlobContainer.GetBlobClient(blobName);
            BlobClient destBlobClient = destinationBlobContainer.GetBlobClient(blobName);

            Helpers.WriteLine($"({progress}) Copying '{blobName}'...", 4);

            if (!destBlobClient.Exists())
            {
                Response<BlobProperties> blobProperties = sourceBlobClient.GetProperties();
                Helpers.Write($"{blobProperties.Value.ContentLength:n0} byte(s): ", 5);

                if (Miscellaneous.UploadMedium.Equals(AppSettings.UploadMedium.LOCAL_NETWORK, StringComparison.InvariantCultureIgnoreCase))
                {
                    CopyBlockBlobUsingLocalNetwork(sourceBlobClient, destBlobClient);
                }
                else
                {
                    CopyBlockBlobBetweenDCs(sourceBlobClient, destBlobClient);
                }
            }
            else
            {
                Helpers.WriteLine($"Already exists", 5);
            }
        }

        private void CopyBlockBlobBetweenDCs(BlobClient sourceBlobClient, BlobClient destBlobClient)
        {
            var sourceBlobSAS = sourceBlobClient.GenerateSasUri(BlobSasPermissions.Read | BlobSasPermissions.List, DateTime.Now.AddDays(1));

            destBlobClient.StartCopyFromUri(sourceBlobSAS);

            int lastProgress = 0;
            int tempProgress = 0;
            string[] progress;

            Helpers.Write($"{lastProgress}%", 1);

            while (true)
            {
                var destBlobProperties = destBlobClient.GetProperties();

                if (destBlobProperties.Value.BlobCopyStatus == CopyStatus.Success)
                {
                    Helpers.WriteLine($" --> 100%", 1);
                    break;
                }
                else if (destBlobProperties.Value.BlobCopyStatus == CopyStatus.Pending)
                {
                    if (destBlobProperties.Value.CopyProgress != null)
                    {
                        progress = destBlobProperties.Value.CopyProgress.Split('/');
                        tempProgress = (int)(Convert.ToDecimal(progress[0]) / Convert.ToDecimal(progress[1]) * 100);

                        //To reduce traces, only write when progress increased >=10
                        if (tempProgress >= (lastProgress + 10))
                        {
                            lastProgress = tempProgress;
                            Helpers.Write($" --> {lastProgress}%", 1);
                        }
                    }

                    System.Threading.Thread.Sleep(100);
                }
                else
                {
                    Console.WriteLine($" --> Copying stopped {destBlobProperties.Value.BlobCopyStatus} - {destBlobProperties.Value.CopyStatusDescription}");
                    break;
                }
            }
        }

        private void CopyAssetAzCopy(BlobContainerClient sourceContainer, BlobContainerClient destinationContainer)
        {
            //Generate a read SAS token for source container that expired after 1 day
            Uri sourceSAS = sourceContainer.GenerateSasUri(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List, new DateTimeOffset(DateTime.Now.AddDays(1))
            );

            //Generate read and write SAS token for destination container that expired after 1 day
            Uri destinationSAS = destinationContainer.GenerateSasUri(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.List | BlobContainerSasPermissions.Write,
                new DateTimeOffset(DateTime.Now.AddDays(1)));

            string copyCommand = string.Format(AZ_COPY_COMMAND, Miscellaneous.AzCopyExePath, sourceSAS, destinationSAS, Miscellaneous.AzCopyPreserveAccessTier);
            bool writeOutput = false;
            bool copyFinished = false;

            using (Process cmd = new Process())
            {
                cmd.StartInfo.FileName = "cmd.exe";
                cmd.StartInfo.RedirectStandardInput = true;
                cmd.StartInfo.RedirectStandardOutput = true;
                cmd.StartInfo.CreateNoWindow = true;
                cmd.StartInfo.UseShellExecute = false;
                cmd.Start();
                cmd.StandardInput.WriteLine(copyCommand);

                while (copyFinished == false)
                {
                    string azCopyOutputLine = cmd.StandardOutput.ReadLine();

                    //Skip traces before starting the copying job
                    if (azCopyOutputLine.Contains("Job", StringComparison.InvariantCultureIgnoreCase) && azCopyOutputLine.Contains("has started", StringComparison.InvariantCultureIgnoreCase))
                    {
                        writeOutput = true;
                    }

                    if (writeOutput && string.IsNullOrEmpty(azCopyOutputLine.Trim()) == false)
                    {
                        Helpers.WriteLine($"--> {azCopyOutputLine}", 4);
                    }

                    //Check when the copy job end
                    if (azCopyOutputLine.Contains("Final Job Status:", StringComparison.InvariantCultureIgnoreCase))
                    {
                        copyFinished = true;
                    }
                }
            }
        }

        private void CopyBlockBlobUsingLocalNetwork(BlobClient sourceBlobClient, BlobClient destBlockBlob)
        {
            using (var readStream = sourceBlobClient.OpenRead())
            {
                var sourceBlobProperties = sourceBlobClient.GetProperties();
                var totalSize = sourceBlobProperties.Value.ContentLength;

                UploadProgressHandler uploadProgressHandler = new UploadProgressHandler(totalSize);

                BlobUploadOptions blobUploadOptions = new BlobUploadOptions();
                blobUploadOptions.ProgressHandler = uploadProgressHandler;
                blobUploadOptions.TransferOptions = new StorageTransferOptions()
                {
                    MaximumTransferSize = 4 * 1024 * 1024,
                    InitialTransferSize = 4 * 1024 * 1024,
                    MaximumConcurrency = 10
                };


                var uploadResult = destBlockBlob.UploadAsync(readStream, blobUploadOptions).Result;

                uploadProgressHandler = null;
            }
        }

        private void ReplicateAssetFilter(string assetName)
        {
            var sourceAssetFiltersPage = _sourceAssetFilterOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName, assetName);
            ReplicateAssetFiltersPage(assetName, sourceAssetFiltersPage);

            while (sourceAssetFiltersPage.NextPageLink != null)
            {
                sourceAssetFiltersPage = _sourceAssetFilterOperations.ListNext(sourceAssetFiltersPage.NextPageLink);
                ReplicateAssetFiltersPage(assetName, sourceAssetFiltersPage);
            }
        }

        private void ReplicateAssetFiltersPage(string assetName, IPage<AssetFilter> assetFilterPage)
        {
            if (assetFilterPage.Any())
            {
                foreach (var assetFilter in assetFilterPage)
                {
                    Helpers.WriteLine($"Copying filter {assetFilter.Name}...", 4);
                    if (_destinationAssetFilterOperations.Get(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, assetName, assetFilter.Name) == null)
                    {
                        _destinationAssetFilterOperations.CreateOrUpdate(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, assetName, assetFilter.Name, assetFilter);
                        Helpers.WriteLine("Done", 5);
                    }
                    else
                    {
                        Helpers.WriteLine($"Already exists", 5);
                    }
                }
            }
            else
            {
                Helpers.WriteLine($"No filters to copy", 4);
            }
        }
    }
}
