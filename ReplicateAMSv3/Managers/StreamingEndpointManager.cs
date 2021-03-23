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
        public override bool Replicate()
        {
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

                    if (DestinationOperations.Get(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, streamingEndpoint.Name) == null)
                    {
                        streamingEndpoint.Location = DestinationAuth.Location;

                        DestinationOperations.Create(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, streamingEndpoint.Name, streamingEndpoint);
                        Helpers.WriteLine("Done", 3);
                    }
                    else
                    {
                        Helpers.WriteLine("Already exists", 3);
                    }
                }
            }
            else
            {
                Helpers.WriteLine("No streaming endpoints to copy", 2);
            }
        }
    }
}
