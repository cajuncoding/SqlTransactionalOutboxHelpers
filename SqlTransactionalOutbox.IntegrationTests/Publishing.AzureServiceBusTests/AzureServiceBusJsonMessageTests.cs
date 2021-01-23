﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SqlTransactionalOutbox.Tests;
using SqlTransactionalOutbox.AzureServiceBus;

namespace SqlTransactionalOutbox.IntegrationTests
{
    [TestClass]
    public class AzureServiceBusJsonMessageTests
    {
        public const string IntegrationTestTopic = "SqlTransactionalOutbox/Integration-Tests";

        public TestContext TestContext { get; set; }

        [TestMethod]
        public async Task TestAzureServiceBusJsonPayloadPublishing()
        {
            dynamic jsonPayload = new JObject();
            jsonPayload.To = "CajunCoding";
            jsonPayload.ContentType = MessageContentTypes.PlainText;
            jsonPayload.Body = "Testing publishing of Json Payload with PlainText Body!";

            var uniqueIdGuidFactory = new OutboxItemUniqueIdentifierGuidFactory();
            var outboxItemFactory = new OutboxItemFactory<Guid, string>(uniqueIdGuidFactory);

            var outboxItem = outboxItemFactory.CreateExistingOutboxItem(
                uniqueIdGuidFactory.CreateUniqueIdentifier(),
                DateTime.UtcNow,
                OutboxItemStatus.Pending.ToString(),
                0,
                IntegrationTestTopic,
                jsonPayload.ToString()
            );

            var options = new AzureServiceBusPublishingOptions()
            {
                LogDebugCallback = s => TestContext.WriteLine(s),
                LogErrorCallback = e => TestContext.WriteLine(e.Message + e.InnerException?.Message)
            };

            var azureServiceBusPublisher = new AzureServiceBusGuidOutboxPublisher(TestConfiguration.AzureServiceBusConnectionString);

            //Execute the publish to Azure...
            await azureServiceBusPublisher.PublishOutboxItemAsync(outboxItem);
        }
    }
}