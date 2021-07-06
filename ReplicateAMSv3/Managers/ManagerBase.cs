using System;
using System.Collections.Generic;
using System.Text;

namespace ReplicateAMSv3.Managers
{
    public abstract class ManagerBase<T> where T : class
    {
        public ServicePrincipalAuth SourceAuth { get; private set; }

        public ServicePrincipalAuth DestinationAuth { get; private set; }

        public Miscellaneous Miscellaneous { get; private set; }

        public T SourceOperations { get; private set; }

        public T DestinationOperations { get; private set; }

        public abstract bool Replicate();

        public virtual void Initialize(T sourceOperations, T destinationOperations, ServicePrincipalAuth sourceAuth, ServicePrincipalAuth destinationAuth, Miscellaneous miscellaneous)
        {
            SourceAuth = sourceAuth;
            SourceOperations = sourceOperations;
            DestinationAuth = destinationAuth;
            DestinationOperations = destinationOperations;
            Miscellaneous = miscellaneous;
        }
    }
}
