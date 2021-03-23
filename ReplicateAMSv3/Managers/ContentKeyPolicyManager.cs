using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public class ContentKeyPolicyManager : ManagerBase<IContentKeyPoliciesOperations>
    {
        public override bool Replicate()
        {
            IPage<ContentKeyPolicy> contentKeyPolicyPage = SourceOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName);
            ReplicateContentKeyPolicyPage(contentKeyPolicyPage);

            while (contentKeyPolicyPage.NextPageLink != null)
            {
                contentKeyPolicyPage = SourceOperations.ListNext(contentKeyPolicyPage.NextPageLink);
                ReplicateContentKeyPolicyPage(contentKeyPolicyPage);
            }

            return true;
        }

        private void ReplicateContentKeyPolicyPage(IPage<ContentKeyPolicy> contentKeyPolicies)
        {
            if (contentKeyPolicies.Any())
            {
                foreach (var contentKeyPolicy in contentKeyPolicies)
                {
                    Helpers.WriteLine($"Copying content key policy '{contentKeyPolicy.Name}'...", 2);

                    ContentKeyPolicyProperties tempContentKey = SourceOperations.GetPolicyPropertiesWithSecrets(SourceAuth.ResourceGroup, SourceAuth.AccountName, contentKeyPolicy.Name);

                    DestinationOperations.CreateOrUpdate(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, contentKeyPolicy.Name, tempContentKey.Options, tempContentKey.Description);

                    Helpers.WriteLine($"Done", 3);
                }
            }
            else
            {
                Helpers.WriteLine($"No content key policies to replicate", 2);
            }
        }
    }
}
