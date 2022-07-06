using Azure.Storage;
using Azure.Storage.Sas;
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
        
        //0: Source storage account name
        //1: Source container name
        //2: Source container SAS
        //3: Destination storage account name
        //4: Destination storage container name
        //5: Destination container SAS
        //6: Preserve access tier
        private const string AZ_COPY_ARGUMENTS = "copy \"https://{0}.blob.core.windows.net/{1}?{2}\" \"https://{3}.blob.core.windows.net/{4}?{5}\" --recursive --overwrite=ifSourceNewer --s2s-preserve-access-tier={6}";

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

                    if (Miscellaneous.CopyUsingAzCopy)
                    {
                        var sourceSasToken = GetAccountSASToken(new StorageSharedKeyCredential(SourceAuth.StorageAccountName, SourceAuth.StorageAccountKey));
                        var targetSasToken = GetAccountSASToken(new StorageSharedKeyCredential(DestinationAuth.StorageAccountName, DestinationAuth.StorageAccountKey));
                        CopyAssetWithAzCopy(ParseAzCopyArgumentString(SourceAuth.StorageAccountName, asset.Container, sourceSasToken, DestinationAuth.StorageAccountName, destinationAsset.Container, targetSasToken));
                    }
                    else
                    {
                        CloudBlobContainer sourceContainer = GetCloudBlobContainer(SourceAuth, asset.Container);
                        CloudBlobContainer destinationContainer = GetCloudBlobContainer(DestinationAuth, destinationAsset.Container);

                        CopyAsset(sourceContainer, destinationContainer);
                    }                    

                    if (_sourceAssetFilterOperations != null && _destinationAssetFilterOperations != null)
                    {
                        Helpers.WriteLine($"Copying asset's filters...", 3);
                        ReplicateAssetFilter(asset.Name);
                    }
                    Helpers.WriteLine($"Replicating asset '{asset.Name}' finished", 2);
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
            CloudBlockBlob sourceBlobClient = sourceBlobContainer.GetBlockBlobReference(blobName);
            CloudBlockBlob destBlockBlob = destinationBlobContainer.GetBlockBlobReference(blobName);

            Helpers.WriteLine($"Copying '{blobName}'...", 4);

            if (!destBlockBlob.Exists())
            {
                sourceBlobClient.FetchAttributes();
                Helpers.Write($"{sourceBlobClient.Properties.Length:n0} byte(s): ", 5);

                if (Miscellaneous.CopyUsingLocalNetwork)
                {
                    CopyBlockBlobUsingLocalNetwork(sourceBlobClient, destBlockBlob);
                }
                else
                {
                    CopyBlockBlobBetweenDCs(sourceBlobClient, destBlockBlob);
                }
            }
            else
            {
                Helpers.WriteLine($"Already exists", 5);
            }
        }

        private void CopyBlockBlobBetweenDCs(CloudBlockBlob sourceBlobClient, CloudBlockBlob destBlockBlob)
        {
            var sourceBlobSAS = sourceBlobClient.GetSharedAccessSignature(new SharedAccessBlobPolicy()
            {
                Permissions = SharedAccessBlobPermissions.Read | SharedAccessBlobPermissions.List,
                SharedAccessStartTime = DateTime.Now.AddMinutes(-5),
                SharedAccessExpiryTime = DateTime.Now.AddDays(1)
            });

            destBlockBlob.StartCopy(new Uri(sourceBlobClient.Uri + sourceBlobSAS));

            int lastProgress = 0;
            int tempProgress = 0;

            Helpers.Write($"{lastProgress}%", 1);

            while (true)
            {
                destBlockBlob.FetchAttributes();

                if (destBlockBlob.CopyState.Status == CopyStatus.Success)
                {
                    Helpers.WriteLine($" --> 100%", 1);
                    break;
                }
                else if (destBlockBlob.CopyState.Status == CopyStatus.Pending)
                {
                    if (destBlockBlob.CopyState.BytesCopied != null && destBlockBlob.CopyState.TotalBytes != null)
                    {
                        tempProgress = (int)(Convert.ToDecimal(destBlockBlob.CopyState.BytesCopied) / Convert.ToDecimal(destBlockBlob.CopyState.TotalBytes) * 100);

                        //To reduce traces, only write when progress increased >=10
                        if (tempProgress >= (lastProgress + 10))
                        {
                            lastProgress = tempProgress;
                            Helpers.Write($" --> {lastProgress}%", 1);
                        }
                    }

                    System.Threading.Thread.Sleep(500);
                }
                else
                {
                    Console.WriteLine($" --> Copying stopped {{destBlockBlob.CopyState.Status - destBlockBlob.CopyState.StatusDescription}}");
                    break;
                }
            }
        }

        private void CopyBlockBlobUsingLocalNetwork(CloudBlockBlob sourceBlobClient, CloudBlockBlob destBlockBlob)
        {
            using (var readStream = sourceBlobClient.OpenRead())
            {
                var totalSize = sourceBlobClient.Properties.Length;
                UploadProgress uploadProgress = new UploadProgress(totalSize);
                BlobRequestOptions blobRequestOptions = new BlobRequestOptions();
                blobRequestOptions.MaximumExecutionTime = new TimeSpan(1, 0, 0);
                blobRequestOptions.NetworkTimeout = new TimeSpan(0, 20, 0);
                blobRequestOptions.ServerTimeout = new TimeSpan(0, 20, 0);

                destBlockBlob.UploadFromStreamAsync(readStream, totalSize, null, blobRequestOptions, null, uploadProgress, new System.Threading.CancellationToken()).Wait();
                uploadProgress = null;
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

        private static string GetAccountSASToken(StorageSharedKeyCredential key)
        {
            AccountSasBuilder sasBuilder = new AccountSasBuilder()
            {
                Services = AccountSasServices.All,
                ResourceTypes = AccountSasResourceTypes.All,
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(1),
                Protocol = SasProtocol.Https
            };
            sasBuilder.SetPermissions(AccountSasPermissions.All);
            string sasToken = sasBuilder.ToSasQueryParameters(key).ToString();
            return sasToken;
        }

        private string ParseAzCopyArgumentString(string sourceBlobStorageName, string sourceContainerName, string sourceSasToken, string targetBlobStorageName, string targetContainerName, string targetSasToken, bool preseveAccessTier = true)
        {
            return string.Format(AZ_COPY_ARGUMENTS, sourceBlobStorageName, sourceContainerName, sourceSasToken, targetBlobStorageName, targetContainerName, targetSasToken, preseveAccessTier);
        }

        private void CopyAssetWithAzCopy(string azcopyArguments)
        {
            var process = new Process();
            var startInfo = new ProcessStartInfo
            {
                FileName = "azcopy.exe",
                Arguments = $"{azcopyArguments}"
            };
            process.StartInfo = startInfo;
            process.Start();
            process.WaitForExit();
        }
    }
}
