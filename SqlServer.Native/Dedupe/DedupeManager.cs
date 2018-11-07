﻿using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
#if (SqlServerDedupe)
namespace NServiceBus.Transport.SqlServerDeduplication
#else
namespace NServiceBus.Transport.SqlServerNative
#endif
{
    public class DedupeManager
    {
        const string writeSqlFormat = @"insert into {0} (Id, Context) values (@Id, @Context);";
        const string readSqlFormat = @"select Context from {0} where Id = @Id";
        string writeSql;
        string readSql;

        SqlConnection connection;
        Table table;
        SqlTransaction transaction;

        public DedupeManager(SqlConnection connection, Table table)
        {
            Guard.AgainstNull(table, nameof(table));
            Guard.AgainstNull(connection, nameof(connection));
            this.connection = connection;
            this.table = table;
            InitSql();
        }

        public DedupeManager(SqlTransaction transaction, Table table)
        {
            Guard.AgainstNull(table, nameof(table));
            Guard.AgainstNull(transaction, nameof(transaction));
            this.transaction = transaction;
            this.table = table;
            connection = transaction.Connection;
            InitSql();
        }

        void InitSql()
        {
            writeSql = ConnectionHelpers.WrapInNoCount(string.Format(writeSqlFormat, table));
            readSql = ConnectionHelpers.WrapInNoCount(string.Format(readSqlFormat, table));
        }

        SqlCommand BuildReadCommand(Guid messageId)
        {
            var command = connection.CreateCommand(transaction, readSql);
            command.Parameters.Add("Id", SqlDbType.UniqueIdentifier).Value = messageId;
            return command;
        }

        SqlCommand BuildWriteCommand(Guid messageId, string context)
        {
            var command = connection.CreateCommand(transaction, writeSql);
            var parameters = command.Parameters;
            parameters.Add("Id", SqlDbType.UniqueIdentifier).Value = messageId;
            var contextParam = parameters.Add("Context", SqlDbType.NVarChar);
            if (context == null)
            {
                contextParam.Value = DBNull.Value;
            }
            else
            {
                contextParam.Value = context;
            }

            return command;
        }

        public async Task<string> ReadContext(Guid messageId, CancellationToken cancellation = default)
        {
            Guard.AgainstEmpty(messageId, nameof(messageId));
            using (var command = BuildReadCommand(messageId))
            {
                var o = await command.ExecuteScalarAsync(cancellation).ConfigureAwait(false);
                if (o == DBNull.Value)
                {
                    return null;
                }
                return (string) o;
            }
        }

        public async Task<DedupeResult> WriteDedupRecord(Guid messageId, string context, CancellationToken cancellation = default)
        {
            Guard.AgainstEmpty(messageId, nameof(messageId));
            try
            {
                using (var command = BuildWriteCommand(messageId, context))
                {
                    await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
                }
            }
            catch (SqlException sqlException)
            {
                if (sqlException.IsKeyViolation())
                {
                    return await BuildDedupeResult(messageId, cancellation);
                }

                throw;
            }

            return new DedupeResult
            {
                DedupeOutcome = DedupeOutcome.Sent,
                Context = context
            };
        }

        async Task<DedupeResult> BuildDedupeResult(Guid messageId, CancellationToken cancellation = default)
        {
            return new DedupeResult
            {
                DedupeOutcome = DedupeOutcome.Deduplicated,
                Context = await ReadContext(messageId, cancellation).ConfigureAwait(false)
            };
        }

        public async Task<DedupeResult> CommitWithDedupCheck(Guid messageId, string context)
        {
            Guard.AgainstEmpty(messageId, nameof(messageId));
            try
            {
                transaction.Commit();
            }
            catch (SqlException sqlException)
            {
                if (sqlException.IsKeyViolation())
                {
                    return await BuildDedupeResult(messageId).ConfigureAwait(false);
                }

                throw;
            }

            return new DedupeResult
            {
                DedupeOutcome = DedupeOutcome.Sent,
                Context = context
            };
        }

        public virtual async Task CleanupItemsOlderThan(DateTime dateTime, CancellationToken cancellation = default)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"delete from {table} where Created < @date";
                command.Parameters.Add("date", SqlDbType.DateTime2).Value = dateTime;
                await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
            }
        }

        public virtual async Task PurgeItems(CancellationToken cancellation = default)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = $"delete from {table}";
                await command.ExecuteNonQueryAsync(cancellation).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Drops a queue.
        /// </summary>
        public virtual Task Drop(CancellationToken cancellation = default)
        {
            return connection.DropTable(transaction, table, cancellation);
        }

        /// <summary>
        /// Creates a queue.
        /// </summary>
        public virtual Task Create(CancellationToken cancellation = default)
        {
            var command = string.Format(DedupeTableSql, table);
            return connection.ExecuteCommand(transaction, command, cancellation);
        }

        /// <summary>
        /// The sql statements used to create the deduplication table.
        /// </summary>
        public static readonly string DedupeTableSql = @"
if exists (
    select *
    from sys.objects
    where object_id = object_id('{0}')
        and type in ('U'))
begin
    if col_length('{0}', 'Context') is null
    begin
        alter table {0}
        add Context nvarchar(max)
    end
    return
end
else
begin
    create table {0} (
        Id uniqueidentifier primary key,
        Created datetime2(0) not null default sysutcdatetime(),
        Context nvarchar(max),
    );
end
";
    }
}