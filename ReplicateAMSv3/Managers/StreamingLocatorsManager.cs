using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public class StreamingLocatorsManager : ManagerBase<IStreamingLocatorsOperations>
    {

        public override bool Replicate()
        {
            var sourceAssetStreamingLocatorsPage = SourceOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName);
            ReplicateStreamingLocatorsPage(sourceAssetStreamingLocatorsPage);

            while (sourceAssetStreamingLocatorsPage.NextPageLink != null)
            {
                sourceAssetStreamingLocatorsPage = SourceOperations.ListNext(sourceAssetStreamingLocatorsPage.NextPageLink);
                ReplicateStreamingLocatorsPage(sourceAssetStreamingLocatorsPage);
            }

            return true;
        }

        private void ReplicateStreamingLocatorsPage(IPage<StreamingLocator> streamingLocatorsPage)
        {
            if (streamingLocatorsPage.Any())
            {
                foreach (var streamingLocator in streamingLocatorsPage)
                {
                    Helpers.WriteLine($"Copying streaming locator '{streamingLocator.Name}'...", 2);

                    if (DestinationOperations.List(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, $"Name eq '{streamingLocator.Name}'").FirstOrDefault() == null)
                    {
                        ListContentKeysResponse listContentKeysResponse = SourceOperations.ListContentKeys(SourceAuth.ResourceGroup, SourceAuth.AccountName, streamingLocator.Name);

                        List<StreamingLocatorContentKey> contentKeyList = null;

                        if (listContentKeysResponse.ContentKeys.Any())
                        {
                            contentKeyList = new List<StreamingLocatorContentKey>();
                            foreach (var contentKey in listContentKeysResponse.ContentKeys)
                            {
                                contentKeyList.Add(new StreamingLocatorContentKey(Guid.NewGuid(), contentKey.Type, contentKey.LabelReferenceInStreamingPolicy, contentKey.Value, contentKey.PolicyName, contentKey.Tracks));
                            }
                        }

                        DestinationOperations.Create(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, streamingLocator.Name, new StreamingLocator()
                        {
                            AssetName = streamingLocator.AssetName,
                            StreamingPolicyName = streamingLocator.StreamingPolicyName,
                            StartTime = streamingLocator.StartTime,
                            EndTime = streamingLocator.EndTime,
                            DefaultContentKeyPolicyName = streamingLocator.DefaultContentKeyPolicyName,
                            ContentKeys = contentKeyList,
                            Filters = streamingLocator.Filters.Count == 0 ? null : streamingLocator.Filters
                        });

                        Helpers.WriteLine("Done", 3);
                    }
                    else
                    {
                        Helpers.WriteLine($"Already exists", 3);
                    }
                }
            }
            else
            {
                Helpers.WriteLine($"No streaming locators to replicate", 2);
            }
        }
    }
}
