using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public class AssetManager : ManagerBase<IAssetsOperations>
    {
        private IAssetFiltersOperations _sourceAssetFilterOperations;
        private IAssetFiltersOperations _destinationAssetFilterOperations;

        public void Initialize(IAssetsOperations sourceOperations, IAssetsOperations destinationOperations, ServicePrincipalAuth sourceAuth, ServicePrincipalAuth destinationAuth, IAssetFiltersOperations sourceAssetFiltersOperations, IAssetFiltersOperations destinationAssetFiltersOperations)
        {
            Initialize(sourceOperations, destinationOperations, sourceAuth, destinationAuth);

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
            var sourceBlobClient = sourceBlobContainer.GetBlockBlobReference(blobName);
            CloudBlockBlob destBlockBlob = destinationBlobContainer.GetBlockBlobReference(blobName);

            Helpers.WriteLine($"Copying '{blobName}'...", 4);

            if (!destBlockBlob.Exists())
            {
                using (var readStream = sourceBlobClient.OpenRead())
                {
                    var totalSize = readStream.Length;
                    UploadProgress uploadProgress = new UploadProgress(totalSize);
                    destBlockBlob.UploadFromStreamAsync(readStream, totalSize, null, null, null, uploadProgress, new System.Threading.CancellationToken()).Wait();
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
