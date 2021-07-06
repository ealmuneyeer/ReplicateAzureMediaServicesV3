using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

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
        private const string AZ_COPY_COMMAND = "\"{0}\" copy \"https://{1}.blob.core.windows.net/{2}{3}\" \"https://{4}.blob.core.windows.net/{5}{6}\" --recursive --overwrite=ifSourceNewer --s2s-preserve-access-tier={7}";

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
                    Helpers.WriteLine($"Copying asset's blobs...", 3);

                    Asset destinationAsset = DestinationOperations.Get(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, asset.Name);

                    //Create destination asset if it does not exists
                    if (destinationAsset == null)
                    {
                        destinationAsset = DestinationOperations.CreateOrUpdate(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, asset.Name, new Asset() { AlternateId = asset.AlternateId, Description = asset.Description });
                    }

                    //Used to update asset info. sometimes it will be empty
                    destinationAsset = DestinationOperations.Get(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, destinationAsset.Name);

                    CloudBlobContainer sourceContainer = GetCloudBlobContainer(SourceAuth, asset.Container);
                    CloudBlobContainer destinationContainer = GetCloudBlobContainer(DestinationAuth, destinationAsset.Container);

                    if (Miscellaneous.UseAzCopy)
                    {
                        CopyAssetAzCopy(sourceContainer, destinationContainer);
                    }
                    else
                    {
                        CopyAsset(sourceContainer, destinationContainer);
                    }

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

        private CloudBlobContainer GetCloudBlobContainer(ServicePrincipalAuth servicePrincipalAuth, string containerName)
        {
            return new CloudBlobContainer(new Uri(servicePrincipalAuth.StorageAccountUrl.ToString() + containerName), new StorageCredentials(servicePrincipalAuth.StorageAccountName, servicePrincipalAuth.StorageAccountKey));
        }

        private void CopyAsset(CloudBlobContainer sourceContainer, CloudBlobContainer destinationContainer)
        {
            var sourceBlobs = sourceContainer.ListBlobs();

            if (sourceBlobs.Any())
            {
                foreach (var sourceBlobItem in sourceBlobs)
                {
                    CopyBlobItem(sourceBlobItem, sourceContainer, destinationContainer);
                }
            }
            else
            {
                Helpers.WriteLine($"No blobs to copy", 4);
            }
        }

        private void CopyAssetAzCopy(CloudBlobContainer sourceContainer, CloudBlobContainer destinationContainer)
        {
            //Generate a read SAS token for source container that expired after 1 day
            string sourceSAS = sourceContainer.GetSharedAccessSignature(new SharedAccessBlobPolicy()
            {
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List,
                SharedAccessStartTime = new DateTimeOffset(DateTime.Now.AddMinutes(-5)),
                SharedAccessExpiryTime = new DateTimeOffset(DateTime.Now.AddDays(1))
            });

            //Generate read and write SAS token for destination container that expired after 1 day
            string destinationSAS = destinationContainer.GetSharedAccessSignature(new SharedAccessBlobPolicy()
            {
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List | SharedAccessBlobPermissions.Write,
                SharedAccessStartTime = new DateTimeOffset(DateTime.Now.AddMinutes(-5)),
                SharedAccessExpiryTime = new DateTimeOffset(DateTime.Now.AddDays(1))
            });

            string copyCommand = string.Format(AZ_COPY_COMMAND, Miscellaneous.AzCopyExe, SourceAuth.StorageAccountName, sourceContainer.Name, sourceSAS, DestinationAuth.StorageAccountName, destinationContainer.Name, destinationSAS, Miscellaneous.AzCopyPreserveAccessTier);
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

        private void CopyBlobItem(IListBlobItem blobItem, CloudBlobContainer sourceBlobContainer, CloudBlobContainer destinationBlobContainer)
        {
            if (blobItem.GetBlobType() == Extensions.BlobType.Directory)
            {
                CopyBlobDirectory(sourceBlobContainer, destinationBlobContainer, blobItem as CloudBlobDirectory);
            }
            else
            {
                var blobName = blobItem.Uri.Segments[^1];
                CopyBlockBlob(blobName, sourceBlobContainer, destinationBlobContainer);
            }
        }

        private void CopyBlockBlob(string blobName, CloudBlobContainer sourceBlobContainer, CloudBlobContainer destinationBlobContainer)
        {
            var sourceBlobClient = sourceBlobContainer.GetBlockBlobReference(blobName);
            CloudBlockBlob destBlockBlob = destinationBlobContainer.GetBlockBlobReference(blobName);

            Helpers.WriteLine($"Copying '{blobName}'...", 4);

            if (!destBlockBlob.Exists())
            {
                using (var readStream = sourceBlobClient.OpenRead())
                {
                    var totalSize = readStream.Length;
                    UploadProgress uploadProgress = new UploadProgress(totalSize);
                    BlobRequestOptions blobRequestOptions = new BlobRequestOptions();
                    blobRequestOptions.MaximumExecutionTime = new TimeSpan(1, 0, 0);
                    blobRequestOptions.NetworkTimeout = new TimeSpan(0, 20, 0);
                    blobRequestOptions.ServerTimeout = new TimeSpan(0, 20, 0);

                    destBlockBlob.UploadFromStreamAsync(readStream, totalSize, null, blobRequestOptions, null, uploadProgress, new System.Threading.CancellationToken()).Wait();
                    uploadProgress = null;
                }
            }
            else
            {
                Helpers.WriteLine($"Already exists", 5);
            }
        }

        private void CopyBlobDirectory(CloudBlobContainer sourceBlobContainer, CloudBlobContainer destinationBlobContainer, CloudBlobDirectory sourceBlobDirectory)
        {
            Extensions.BlobType blobType;

            foreach (var blobItem in sourceBlobDirectory.ListBlobs())
            {
                blobType = blobItem.GetBlobType();
                if (blobType == Extensions.BlobType.BlockBlock)
                {
                    CopyBlockBlob((blobItem as CloudBlockBlob).Name, sourceBlobContainer, destinationBlobContainer);
                }
                else if (blobType == Extensions.BlobType.Directory)
                {
                    CopyBlobDirectory(sourceBlobContainer, destinationBlobContainer, blobItem as CloudBlobDirectory);
                }
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
