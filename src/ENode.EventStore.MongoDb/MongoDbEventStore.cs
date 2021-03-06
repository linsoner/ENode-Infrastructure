﻿using ECommon.IO;
using ECommon.Logging;
using ECommon.Serializing;
using ENode.Eventing;
using ENode.EventStore.MongoDb.Collections;
using ENode.EventStore.MongoDb.Models;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ENode.EventStore.MongoDb
{
    public class MongoDbEventStore : IEventStore
    {
        #region Private Variables

        private readonly IEventSerializer _eventSerializer;
        private readonly IEventStreamCollection _eventStreamCollection;
        private readonly IOHelper _ioHelper;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILogger _logger;

        #endregion Private Variables

        #region Public Variables

        public bool SupportBatchAppendEvent { get; set; }

        #endregion Public Variables

        #region Ctor

        public MongoDbEventStore(
            IEventSerializer eventSerializer,
            IEventStreamCollection eventStreamCollection,
            IOHelper ioHelper,
            IJsonSerializer jsonSerializer,
            ILoggerFactory loggerFactory
            )
        {
            SupportBatchAppendEvent = false;

            _eventSerializer = eventSerializer;
            _eventStreamCollection = eventStreamCollection;
            _ioHelper = ioHelper;
            _jsonSerializer = jsonSerializer;
            _logger = loggerFactory.Create(GetType().FullName);
        }

        #endregion Ctor

        #region Public Methods

        public Task<AsyncTaskResult<EventAppendResult>> AppendAsync(DomainEventStream eventStream)
        {
            var record = ConvertTo(eventStream);
            return _ioHelper.TryIOFuncAsync(async () =>
            {
                try
                {
                    var collection = _eventStreamCollection.GetCollection(record.AggregateRootId);

                    await collection.InsertOneAsync(record);

                    return new AsyncTaskResult<EventAppendResult>(AsyncTaskStatus.Success, EventAppendResult.Success);
                }
                catch (MongoWriteException ex)
                {
                    if (ex.WriteError.Code == 11000 && ex.Message.Contains(nameof(record.AggregateRootId)) && ex.Message.Contains(nameof(record.Version)))
                    {
                        return new AsyncTaskResult<EventAppendResult>(AsyncTaskStatus.Success, EventAppendResult.DuplicateEvent);
                    }
                    else if (ex.WriteError.Code == 11000 && ex.Message.Contains(nameof(record.AggregateRootId)) && ex.Message.Contains(nameof(record.CommandId)))
                    {
                        return new AsyncTaskResult<EventAppendResult>(AsyncTaskStatus.Success, EventAppendResult.DuplicateCommand);
                    }
                    _logger.Error(string.Format("Append event has write exception, eventStream: {0}", eventStream), ex);
                    return new AsyncTaskResult<EventAppendResult>(AsyncTaskStatus.IOException, ex.Message, EventAppendResult.Failed);
                }
                catch (Exception ex)
                {
                    _logger.Error(string.Format("Append event has unknown exception, eventStream: {0}", eventStream), ex);
                    return new AsyncTaskResult<EventAppendResult>(AsyncTaskStatus.Failed, ex.Message, EventAppendResult.Failed);
                }
            }, "AppendEventsAsync");
        }

        public Task<AsyncTaskResult<EventAppendResult>> BatchAppendAsync(IEnumerable<DomainEventStream> eventStreams)
        {
            if (!SupportBatchAppendEvent)
            {
                throw new NotSupportedException("Unsupport batch append event.");
            }

            throw new NotImplementedException();
        }

        public Task<AsyncTaskResult<DomainEventStream>> FindAsync(string aggregateRootId, int version)
        {
            return _ioHelper.TryIOFuncAsync(async () =>
            {
                try
                {
                    var builder = Builders<EventStream>.Filter;
                    var filter = builder.Eq(e => e.AggregateRootId, aggregateRootId) & builder.Eq(e => e.Version, version);
                    var result = await _eventStreamCollection.GetCollection(aggregateRootId).FindAsync(filter);
                    var record = result.SingleOrDefault();
                    var stream = record != null ? ConvertFrom(record) : null;
                    return new AsyncTaskResult<DomainEventStream>(AsyncTaskStatus.Success, stream);
                }
                catch (MongoQueryException ex)
                {
                    _logger.Error(string.Format("Find event by version has query exception, aggregateRootId: {0}, version: {1}", aggregateRootId, version), ex);
                    return new AsyncTaskResult<DomainEventStream>(AsyncTaskStatus.IOException, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.Error(string.Format("Find event by version has unknown exception, aggregateRootId: {0}, version: {1}", aggregateRootId, version), ex);
                    return new AsyncTaskResult<DomainEventStream>(AsyncTaskStatus.Failed, ex.Message);
                }
            }, "FindEventByVersionAsync");
        }

        public Task<AsyncTaskResult<DomainEventStream>> FindAsync(string aggregateRootId, string commandId)
        {
            return _ioHelper.TryIOFuncAsync(async () =>
            {
                try
                {
                    var builder = Builders<EventStream>.Filter;
                    var filter = builder.Eq(e => e.AggregateRootId, aggregateRootId) & builder.Eq(e => e.CommandId, commandId);
                    var result = await _eventStreamCollection.GetCollection(aggregateRootId).FindAsync(filter);
                    var record = result.SingleOrDefault();
                    var stream = record != null ? ConvertFrom(record) : null;
                    return new AsyncTaskResult<DomainEventStream>(AsyncTaskStatus.Success, stream);
                }
                catch (MongoQueryException ex)
                {
                    _logger.Error(string.Format("Find event by commandId has query exception, aggregateRootId: {0}, commandId: {1}", aggregateRootId, commandId), ex);
                    return new AsyncTaskResult<DomainEventStream>(AsyncTaskStatus.IOException, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.Error(string.Format("Find event by commandId has unknown exception, aggregateRootId: {0}, commandId: {1}", aggregateRootId, commandId), ex);
                    return new AsyncTaskResult<DomainEventStream>(AsyncTaskStatus.Failed, ex.Message);
                }
            }, "FindEventByCommandIdAsync");
        }

        public IEnumerable<DomainEventStream> QueryAggregateEvents(string aggregateRootId, string aggregateRootTypeName, int minVersion, int maxVersion)
        {
            return _ioHelper.TryIOFunc(() =>
            {
                try
                {
                    var builder = Builders<EventStream>.Filter;

                    var filter = builder.Eq(e => e.AggregateRootId, aggregateRootId)
                    & builder.Gte(e => e.Version, minVersion)
                    & builder.Lte(e => e.Version, maxVersion);

                    var sort = Builders<EventStream>.Sort.Ascending(e => e.Version);

                    var result = _eventStreamCollection.GetCollection(aggregateRootId)
                    .Find(filter)
                    .Sort(sort)
                    .ToList();

                    return result.Select(record => ConvertFrom(record));
                }
                catch (MongoQueryException ex)
                {
                    var errorMessage = string.Format("Failed to query aggregate events, aggregateRootId: {0}, aggregateRootType: {1}", aggregateRootId, aggregateRootTypeName);
                    _logger.Error(errorMessage, ex);
                    throw new IOException(errorMessage, ex);
                }
                catch (Exception ex)
                {
                    var errorMessage = string.Format("Failed to query aggregate events, aggregateRootId: {0}, aggregateRootType: {1}", aggregateRootId, aggregateRootTypeName);
                    _logger.Error(errorMessage, ex);
                    throw;
                }
            }, "QueryAggregateEvents");
        }

        public Task<AsyncTaskResult<IEnumerable<DomainEventStream>>> QueryAggregateEventsAsync(string aggregateRootId, string aggregateRootTypeName, int minVersion, int maxVersion)
        {
            return _ioHelper.TryIOFuncAsync(async () =>
            {
                try
                {
                    var builder = Builders<EventStream>.Filter;

                    var filter = builder.Eq(e => e.AggregateRootId, aggregateRootId)
                    & builder.Gte(e => e.Version, minVersion)
                    & builder.Lte(e => e.Version, maxVersion);

                    var sort = Builders<EventStream>.Sort.Ascending(e => e.Version);

                    var result = await _eventStreamCollection.GetCollection(aggregateRootId)
                    .Find(filter)
                    .Sort(sort)
                    .ToListAsync();

                    var streams = result.Select(ConvertFrom);
                    return new AsyncTaskResult<IEnumerable<DomainEventStream>>(AsyncTaskStatus.Success, streams);
                }
                catch (MongoQueryException ex)
                {
                    var errorMessage = string.Format("Failed to query aggregate events async, aggregateRootId: {0}, aggregateRootType: {1}", aggregateRootId, aggregateRootTypeName);
                    _logger.Error(errorMessage, ex);
                    return new AsyncTaskResult<IEnumerable<DomainEventStream>>(AsyncTaskStatus.IOException, ex.Message);
                }
                catch (Exception ex)
                {
                    var errorMessage = string.Format("Failed to query aggregate events async, aggregateRootId: {0}, aggregateRootType: {1}", aggregateRootId, aggregateRootTypeName);
                    _logger.Error(errorMessage, ex);
                    return new AsyncTaskResult<IEnumerable<DomainEventStream>>(AsyncTaskStatus.Failed, ex.Message);
                }
            }, "QueryAggregateEventsAsync");
        }

        #endregion Public Methods

        #region Private Methods

        private DomainEventStream ConvertFrom(EventStream record)
        {
            return new DomainEventStream(
                record.CommandId,
                record.AggregateRootId,
                record.AggregateRootTypeName,
                record.Version,
                record.CreatedOn,
                _eventSerializer.Deserialize<IDomainEvent>(_jsonSerializer.Deserialize<IDictionary<string, string>>(record.Events)));
        }

        private EventStream ConvertTo(DomainEventStream eventStream)
        {
            return new EventStream
            {
                CommandId = eventStream.CommandId,
                AggregateRootId = eventStream.AggregateRootId,
                AggregateRootTypeName = eventStream.AggregateRootTypeName,
                Version = eventStream.Version,
                CreatedOn = eventStream.Timestamp,
                Events = _jsonSerializer.Serialize(_eventSerializer.Serialize(eventStream.Events))
            };
        }

        #endregion Private Methods
    }
}