﻿using ENode.AggregateSnapshot.Configuration;
using ENode.Store.MongoDb;

namespace ENode.AggregateSnapshot
{
    public class SnapshotMongoDbProvider : MongoDbProvider, ISnapshotMongoDbProvider
    {
        public SnapshotMongoDbProvider(ISnapshotMongoDbConfiguration configuration) : base(configuration)
        {
        }
    }
}