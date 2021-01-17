﻿using System;
using System.Collections.Generic;
using System.Text;

namespace SqlTransactionalOutboxHelpers
{
    public interface ISqlTransactionalOutboxItemFactory<TUniqueIdentifier, in TPayload>
    {
        ISqlTransactionalOutboxItem<TUniqueIdentifier> CreateNewOutboxItem(
            string publishingTarget,
            TPayload publishingPayload
        );
        
        ISqlTransactionalOutboxItem<TUniqueIdentifier> CreateExistingOutboxItem(
            TUniqueIdentifier uniqueIdentifier,
            string status,
            int publishingAttempts,
            DateTime createdDateTimeUtc,
            string publishingTarget, 
            //NOTE: When Creating an Existing Item we always take in the Serialized Payload
            string serializedPayload
        );
    }
}
