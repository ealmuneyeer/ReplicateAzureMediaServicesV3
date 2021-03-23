using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public class AccountFilterManager : ManagerBase<IAccountFiltersOperations>
    {
        public override bool Replicate()
        {
            IPage<AccountFilter> sourceAccountFilterPage = SourceOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName);
            ReplicateAccountFiltersPage(sourceAccountFilterPage);

            while (sourceAccountFilterPage.NextPageLink != null)
            {
                sourceAccountFilterPage = SourceOperations.ListNext(sourceAccountFilterPage.NextPageLink);
                ReplicateAccountFiltersPage(sourceAccountFilterPage);
            }

            return true;
        }

        private void ReplicateAccountFiltersPage(IPage<AccountFilter> accountFilterPage)
        {
            if (accountFilterPage.Any())
            {
                foreach (var accountFilter in accountFilterPage)
                {
                    Helpers.WriteLine($"Replicating account filter {accountFilter.Name}...", 2);
                    DestinationOperations.CreateOrUpdate(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, accountFilter.Name, accountFilter);
                    Helpers.WriteLine("Done", 3);
                }
            }
            else
            {
                Helpers.WriteLine("No account filters to copy", 2);
            }
        }
    }
}
