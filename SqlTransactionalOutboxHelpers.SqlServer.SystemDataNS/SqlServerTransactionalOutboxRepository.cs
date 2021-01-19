﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using SqlAppLockHelper.SystemDataNS;
using SqlTransactionalOutboxHelpers.CustomExtensions;

namespace SqlTransactionalOutboxHelpers.SqlServer.SystemDataNS
{
    public class SqlServerTransactionalOutboxRepository<TPayload> : BaseSqlServerTransactionalOutboxRepository<Guid, TPayload>, ISqlTransactionalOutboxRepository<Guid, TPayload>
    {
        protected SqlTransaction SqlTransaction { get; set; }
        protected SqlConnection SqlConnection { get; set; }

        public SqlServerTransactionalOutboxRepository(
            SqlTransaction sqlTransaction, 
            ISqlTransactionalOutboxTableConfig outboxTableConfig = null,
            ISqlTransactionalOutboxItemFactory<Guid, TPayload> outboxItemFactory = null,
            int distributedMutexAcquisitionTimeoutSeconds = 5
        )
        {
            SqlTransaction = sqlTransaction ?? 
                throw new ArgumentNullException(nameof(sqlTransaction), "A valid SqlTransaction must be provided for Sql Transactional Outbox processing.");

            SqlConnection = sqlTransaction.Connection ?? 
                throw new ArgumentNullException(nameof(SqlConnection), "The SqlTransaction specified must have a valid SqlConnection.");

            base.Init(
                outboxTableConfig: outboxTableConfig ?? new DefaultOutboxTableConfig(), 
                outboxItemFactory: outboxItemFactory ?? new OutboxItemFactory<TPayload>(), 
                distributedMutexAcquisitionTimeoutSeconds
            );
        }

        public virtual async Task<List<ISqlTransactionalOutboxItem<Guid>>> RetrieveOutboxItemsAsync(OutboxItemStatus status, int maxBatchSize = -1)
        {
            var statusParamName = "status";
            var sql = QueryBuilder.BuildSqlForRetrieveOutboxItemsByStatus(status, maxBatchSize, statusParamName);
            
            await using var sqlCmd = CreateSqlCommand(sql);
            AddParam(sqlCmd, statusParamName, status.ToString());

            var results = new List<ISqlTransactionalOutboxItem<Guid>>();

            await using var sqlReader = await sqlCmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await sqlReader.ReadAsync().ConfigureAwait(false))
            {
                var outboxItem = OutboxItemFactory.CreateExistingOutboxItem(
                    uniqueIdentifier: (Guid)sqlReader[OutboxTableConfig.UniqueIdentifierFieldName],
                    status:(string)sqlReader[OutboxTableConfig.StatusFieldName],
                    publishingAttempts:(int)sqlReader[OutboxTableConfig.PublishingAttemptsFieldName],
                    createdDateTimeUtc:(DateTime)sqlReader[OutboxTableConfig.CreatedDateTimeUtcFieldName],
                    publishingTarget:(string)sqlReader[OutboxTableConfig.PublishingTargetFieldName],
                    serializedPayload:(string)sqlReader[OutboxTableConfig.PublishingPayloadFieldName]
                );

                results.Add(outboxItem);
            }

            return results;
        }

        public virtual async Task CleanupOutboxHistoricalItemsAsync(TimeSpan historyTimeToKeepTimeSpan)
        {
            var purgeHistoryParamName = "@purgeHistoryBeforeDate";
            var purgeHistoryBeforeDate = DateTime.UtcNow.Subtract(historyTimeToKeepTimeSpan);

            var sql = QueryBuilder.BuildSqlForHistoricalOutboxCleanup(purgeHistoryParamName);
            
            await using var sqlCmd = CreateSqlCommand(sql);
            AddParam(sqlCmd, purgeHistoryParamName, purgeHistoryBeforeDate);

            await sqlCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        public virtual async Task<List<ISqlTransactionalOutboxItem<Guid>>> InsertNewOutboxItemsAsync(
            IEnumerable<ISqlTransactionalOutboxInsertionItem<TPayload>> outboxItems, 
            int insertBatchSize = 20
        )
        {
            await using var sqlCmd = CreateSqlCommand("");

            //Use the Outbox Item Factory to create a new Outbox Item with serialized payload.
            var outboxItemsList = outboxItems.Select(
                i => OutboxItemFactory.CreateNewOutboxItem(
                    i.PublishingTarget, 
                    i.PublishingPayload
                )
            ).ToList();

            var batches = outboxItemsList.Chunk(insertBatchSize);
            foreach (var batch in batches)
            {
                sqlCmd.CommandText = QueryBuilder.BuildParameterizedSqlToInsertNewOutboxItems(batch);
                sqlCmd.Parameters.Clear();

                //Add the Parameters!
                for (var batchIndex = 0; batchIndex < batch.Length; batchIndex++)
                {
                    var outboxItem = batch[batchIndex];

                    AddParam(sqlCmd, OutboxTableConfig.UniqueIdentifierFieldName, outboxItem.UniqueIdentifier, batchIndex);
                    //NOTE: The for Sql Server, the CreatedDateTimeUtcField is automatically populated by Sql Server.
                    //      this helps eliminate risks of datetime sequencing across servers or server-less environments.
                    //AddParam(sqlCmd, OutboxTableConfig.CreatedDateTimeUtcFieldName, outboxItem.CreatedDateTimeUtc, batchIndex);
                    AddParam(sqlCmd, OutboxTableConfig.StatusFieldName, outboxItem.Status.ToString(), batchIndex);
                    AddParam(sqlCmd, OutboxTableConfig.PublishingAttemptsFieldName, outboxItem.PublishingAttempts, batchIndex);
                    AddParam(sqlCmd, OutboxTableConfig.PublishingTargetFieldName, outboxItem.PublishingTarget, batchIndex);
                    AddParam(sqlCmd, OutboxTableConfig.PublishingPayloadFieldName, outboxItem.PublishingPayload, batchIndex);
                }

                //Execute the Batch and continue...
                await using var sqlReader = await sqlCmd.ExecuteReaderAsync().ConfigureAwait(false);

                //Since some fields are actually populated in the Database, we post-process to update the models with valid
                //  values as returned from teh Insert process...
                var outboxBatchLookup = batch.ToLookup(i => i.UniqueIdentifier);
                while (await sqlReader.ReadAsync().ConfigureAwait(false))
                {
                    //The First field is always our UniqueIdentifier (as defined by the Output clause of the Sql)
                    // and the Second field is always the UTC Created DateTime returned from the Database.
                    var uniqueIdentifier = sqlReader.GetGuid(0);
                    var createdDateUtcFromDb = sqlReader.GetDateTime(1);

                    var outboxItem = outboxBatchLookup[uniqueIdentifier].First();
                    outboxItem.CreatedDateTimeUtc = createdDateUtcFromDb;
                }
            }

            return outboxItemsList;
        }

        public virtual async Task<List<ISqlTransactionalOutboxItem<Guid>>> UpdateOutboxItemsAsync(
            IEnumerable<ISqlTransactionalOutboxItem<Guid>> outboxItems, int updateBatchSize = 20
        )
        {
            await using var sqlCmd = CreateSqlCommand("");

            var outboxItemsList = outboxItems.ToList();

            var batches = outboxItemsList.Chunk(updateBatchSize);
            foreach (var batch in batches)
            {
                sqlCmd.CommandText = QueryBuilder.BuildParameterizedSqlToUpdateExistingOutboxItem(batch);

                //Add the Parameters!
                var batchIndex = 0;
                foreach (var outboxItem in batch)
                {
                   //NOTE: The only Updateable Fields are Status & PublishingAttempts
                    AddParam(sqlCmd, OutboxTableConfig.StatusFieldName, outboxItem.Status, batchIndex);
                    AddParam(sqlCmd, OutboxTableConfig.PublishingAttemptsFieldName, outboxItem.PublishingAttempts, batchIndex);
                    batchIndex++;
                }

                //Execute the Batch and continue...
                await sqlCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }

            return outboxItemsList;
        }

        public virtual async Task<IAsyncDisposable> AcquireDistributedProcessingMutexAsync()
        {
            var distributedMutex = await SqlTransaction.AcquireAppLockAsync(
                DistributedMutexLockName, 
                DistributedMutexAcquisitionTimeoutSeconds,
                throwsException: false
            );

            //Safely return null if the Lock was not successfully acquired.
            return distributedMutex.IsLockAcquired ? distributedMutex : null;
        }

        #region Helpers
        
        protected SqlCommand CreateSqlCommand(string sqlCmdText)
        {
            var sqlCmd = new SqlCommand(sqlCmdText, this.SqlConnection, this.SqlTransaction)
            {
                CommandType = CommandType.Text
            };
            return sqlCmd;
        }

        protected void AddParam(SqlCommand sqlCmd, string name, object value, int index = -1)
        {
            sqlCmd.Parameters.AddWithValue(
                QueryBuilder.ToSqlParamName(name, index),
                value
            );
        }

        #endregion

    }

    internal class SqlServerInsertedItem
    {
        public int Id { get; set; }
        public Guid Guid { get; set; }
    }
}
