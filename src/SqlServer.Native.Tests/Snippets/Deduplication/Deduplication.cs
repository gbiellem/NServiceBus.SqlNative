﻿using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using NServiceBus.Transport.SqlServerNative;

public class Deduplication
{
    SqlConnection sqlConnection = null!;
    string connectionString = null!;

    async Task CreateTable()
    {
        #region CreateDeduplicationTable

        var manager = new DedupeManager(sqlConnection, "DeduplicationTable");
        await manager.Create();

        #endregion
    }

    async Task DeleteTable()
    {
        #region DeleteDeduplicationTable

        var manager = new DedupeManager(sqlConnection, "DeduplicationTable");
        await manager.Drop();

        #endregion
    }

    async Task Send()
    {
        string headers = null!;
        byte[] body = null!;

        #region SendWithDeduplication

        var manager = new QueueManager(
            "endpointTable",
            sqlConnection,
            "DeduplicationTable");
        var message = new OutgoingMessage(
            id: Guid.NewGuid(),
            headers: headers,
            bodyBytes: body);
        await manager.Send(message);

        #endregion

    }

    async Task DeduplicationCleanerJob()
    {
        #region DeduplicationCleanerJobStart

        var cleaner = new DedupeCleanerJob(
            table: "Deduplication",
            connectionBuilder: cancellation =>
            {
                return ConnectionHelpers.OpenConnection(connectionString, cancellation);
            },
            criticalError: _ => { },
            expireWindow: TimeSpan.FromHours(1),
            frequencyToRunCleanup: TimeSpan.FromMinutes(10));
        cleaner.Start();

        #endregion

        #region DeduplicationCleanerJobStop

        await cleaner.Stop();

        #endregion
    }

    async Task SendBatch()
    {
        string headers1 = null!;
        byte[] body1 = null!;
        string headers2 = null!;
        byte[] body2 = null!;

        #region SendBatchWithDeduplication

        var manager = new QueueManager(
            "endpointTable",
            sqlConnection,
            "DeduplicationTable");
        var messages = new List<OutgoingMessage>
        {
            new(
                id: Guid.NewGuid(),
                headers: headers1,
                bodyBytes: body1),
            new(
                id: Guid.NewGuid(),
                headers: headers2,
                bodyBytes: body2),
        };
        await manager.Send(messages);

        #endregion
    }
}