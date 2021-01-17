﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SqlTransactionalOutboxHelpers
{
    public interface ISqlTransactionalOutboxPublisher<TUniqueIdentifier>
    {
        Task PublishOutboxItemAsync(ISqlTransactionalOutboxItem<TUniqueIdentifier> outboxItem);
    }
}
