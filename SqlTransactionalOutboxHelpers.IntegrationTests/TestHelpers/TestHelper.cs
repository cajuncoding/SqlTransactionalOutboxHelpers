﻿using System;
using System.Collections.Generic;
using System.Text;
using SqlTransactionalOutboxHelpers;
using SqlTransactionalOutboxHelpers.Tests;
using SystemData = System.Data.SqlClient;
//using MicrosoftData = Microsoft.Data.SqlClient;

namespace SqlTransactionalOutboxHelpers.Tests
{
    public class TestHelper
    {
        public static List<OutboxInsertionItem<string>> CreateTestStringOutboxItemData(int dataSize, int targetModulus = 5)
        {
            var list = new List<OutboxInsertionItem<string>>();
            for (var x = 1; x <= dataSize; x++)
            {
                list.Add(new OutboxInsertionItem<string>(
                    $"/publish/target_{(int)dataSize % 5}",
                    $"Payload Message #{x:00000}"
                ));
            }

            return list;
        }
    }
}