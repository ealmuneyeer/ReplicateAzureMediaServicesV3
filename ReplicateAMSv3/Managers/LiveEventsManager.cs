using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public class LiveEventsManager : ManagerBase<ILiveEventsOperations>
    {
        private ILiveOutputsOperations _sourceLiveOutputOperations;
        private ILiveOutputsOperations _destinationLiveOutputOperations;

        public void Initialize(ILiveEventsOperations sourceOperations, ILiveEventsOperations destinationOperations, ServicePrincipalAuth sourceAuth, ServicePrincipalAuth destinationAuth, Miscellaneous miscellaneous, ILiveOutputsOperations sourceLiveOutputOperations, ILiveOutputsOperations destinationLiveOutputOperations)
        {
            Initialize(sourceOperations, destinationOperations, sourceAuth, destinationAuth, miscellaneous);

            _sourceLiveOutputOperations = sourceLiveOutputOperations;
            _destinationLiveOutputOperations = destinationLiveOutputOperations;
        }

        public override bool Replicate()
        {
            IPage<LiveEvent> liveEventsPage = SourceOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName);
            ReplicateLiveEventsPage(liveEventsPage);

            while (liveEventsPage.NextPageLink != null)
            {
                liveEventsPage = SourceOperations.ListNext(liveEventsPage.NextPageLink);
                ReplicateLiveEventsPage(liveEventsPage);
            }

            return true;
        }

        private void ReplicateLiveEventsPage(IPage<LiveEvent> liveEventsPage)
        {
            if (liveEventsPage.Any())
            {
                foreach (var liveEvent in liveEventsPage)
                {
                    Helpers.WriteLine($"Replicating live event '{liveEvent.Name}'...", 2);
                    string tempResult = "";

                    if (DestinationOperations.Get(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, liveEvent.Name) == null)
                    {
                        Helpers.WriteLine($"Copying live event...", 3);

                        LiveEvent tempLiveEvent = new LiveEvent()
                        {
                            CrossSiteAccessPolicies = liveEvent.CrossSiteAccessPolicies,
                            Description = liveEvent.Description,
                            Encoding = liveEvent.Encoding,
                            Input = liveEvent.Input,
                            StreamOptions = liveEvent.StreamOptions,
                            Tags = liveEvent.Tags,
                            Transcriptions = liveEvent.Transcriptions,
                            UseStaticHostname = liveEvent.UseStaticHostname,
                            HostnamePrefix = liveEvent.HostnamePrefix,
                            Location = DestinationAuth.Location,
                            Preview = liveEvent.Preview
                        };

                        tempLiveEvent.Input.Endpoints = null;
                        tempLiveEvent.Preview.PreviewLocator = "";

                        DestinationOperations.Create(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, liveEvent.Name, tempLiveEvent);

                        tempResult = "Done";
                    }
                    else
                    {
                        tempResult = "Already exists";
                    }
                    Helpers.WriteLine(tempResult, 4);

                    ReplicateLiveOutputs(liveEvent.Name);
                }

            }
            else
            {
                Helpers.WriteLine("No live events to copy", 2);
            }
        }

        private void ReplicateLiveOutputs(string liveEventName)
        {
            Helpers.WriteLine("Copying live outputs...", 3);

            IPage<LiveOutput> liveOutputsPage = _sourceLiveOutputOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName, liveEventName);
            ReplicateLiveOutputsPage(liveEventName, liveOutputsPage);

            while (liveOutputsPage.NextPageLink != null)
            {
                liveOutputsPage = _sourceLiveOutputOperations.ListNext(liveOutputsPage.NextPageLink);
                ReplicateLiveOutputsPage(liveEventName, liveOutputsPage);
            }
        }

        private void ReplicateLiveOutputsPage(string liveEventName, IPage<LiveOutput> liveOutputsPage)
        {
            if (liveOutputsPage.Any())
            {
                foreach (var liveOutput in liveOutputsPage)
                {
                    Helpers.WriteLine($"Copying live output {liveOutput.Name}...", 4);
                    string tempResult = "";

                    if (_destinationLiveOutputOperations.Get(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, liveEventName, liveOutput.Name) == null)
                    {
                        _destinationLiveOutputOperations.Create(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, liveEventName, liveOutput.Name, liveOutput);
                        tempResult = "Done";
                    }
                    else
                    {
                        tempResult = "Already exists";
                    }
                    Helpers.WriteLine(tempResult, 5);
                }
            }
            else
            {
                Helpers.WriteLine("No live outputs", 4);
            }
        }
    }
}

