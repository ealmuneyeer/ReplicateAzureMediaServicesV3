using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Rest.Azure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public class TransformManager : ManagerBase<ITransformsOperations>
    {
        public override bool Replicate()
        {
            IPage<Transform> transformPage = SourceOperations.List(SourceAuth.ResourceGroup, SourceAuth.AccountName);
            ReplicateTeansformsPage(transformPage);

            while (transformPage.NextPageLink != null)
            {
                transformPage = SourceOperations.ListNext(transformPage.NextPageLink);
            }

            return true;
        }

        private void ReplicateTeansformsPage(IPage<Transform> transforms)
        {
            if (transforms.Any())
            {
                foreach (var transform in transforms)
                {
                    //Console.WriteLine($"   Copying transform '{transform.Name}'...");
                    Helpers.WriteLine($"Copying transform '{transform.Name}'...", 2);
                    DestinationOperations.CreateOrUpdate(DestinationAuth.ResourceGroup, DestinationAuth.AccountName, transform.Name, transform.Outputs, transform.Description);
                    //Console.WriteLine($"      Done");
                    Helpers.WriteLine($"Done", 3);
                }
            }
            else
            {
                //Console.WriteLine("   No transforms to copy");
                Helpers.WriteLine("No transforms to copy", 2);
            }
        }
    }
}
