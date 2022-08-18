using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public class StreamingEndpointManager : ManagerBase<IStreamingEndpointsOperations>
    {

        private List<StreamingEndpoint> _destinationSEList = new List<StreamingEndpoint>();
        public override bool Replicate()
        {
            FillDestinationSEs();

            IPage<StreamingEndpoint> streamingEndpointsPage = SourceOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName);

            ReplicateStreamingEndpointsPage(streamingEndpointsPage);

            while (streamingEndpointsPage.NextPageLink != null)
            {
                streamingEndpointsPage = SourceOperations.ListNext(streamingEndpointsPage.NextPageLink);
                ReplicateStreamingEndpointsPage(streamingEndpointsPage);
            }

            return true;
        }

        private void ReplicateStreamingEndpointsPage(IPage<StreamingEndpoint> streamingEndpointsPage)
        {
            if (streamingEndpointsPage.Any())
            {
                foreach (var streamingEndpoint in streamingEndpointsPage)
                {
                    Helpers.WriteLine($"Copying streaming endpoint '{streamingEndpoint.Name}'...", 2);

                    if (_destinationSEList.Any(se => se.Name.Equals(streamingEndpoint.Name, StringComparison.InvariantCultureIgnoreCase)))
                    {
                        Helpers.WriteLine("Already exists", 3);
                    }
                    else
                    {
                        StreamingEndpoint se = new StreamingEndpoint()
                        {
                            AccessControl = streamingEndpoint.AccessControl,
                            AvailabilitySetName = streamingEndpoint.AvailabilitySetName,
                            CdnEnabled = streamingEndpoint.CdnEnabled,
                            CdnProfile = streamingEndpoint.CdnProfile,
                            CdnProvider = streamingEndpoint.CdnProvider,
                            CrossSiteAccessPolicies = streamingEndpoint.CrossSiteAccessPolicies,
                            CustomHostNames = streamingEndpoint.CustomHostNames,
                            Description = streamingEndpoint.Description,
                            Location = streamingEndpoint.Location,
                            MaxCacheAge = streamingEndpoint.MaxCacheAge,
                            ScaleUnits = streamingEndpoint.ScaleUnits,
                            Tags = streamingEndpoint.Tags,
                        };

                        DestinationOperations.Create(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, streamingEndpoint.Name, se);
                        Helpers.WriteLine("Done", 3);
                    }
                }
            }
            else
            {
                Helpers.WriteLine("No streaming endpoints to copy", 2);
            }
        }

        private void FillDestinationSEs()
        {
            IPage<StreamingEndpoint> streamingEndpointsPage = DestinationOperations.List(DestinationAuth.ResourceGroup, DestinationAuth.AccountName);

            _destinationSEList.AddRange(streamingEndpointsPage.ToList());

            while (streamingEndpointsPage.NextPageLink != null)
            {
                streamingEndpointsPage = DestinationOperations.ListNext(streamingEndpointsPage.NextPageLink);
                _destinationSEList.AddRange(streamingEndpointsPage.ToList());
            }
        }

    }
}
