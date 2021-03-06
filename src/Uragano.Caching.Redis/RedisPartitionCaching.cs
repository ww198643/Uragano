﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Uragano.Abstractions;
using Uragano.Abstractions.ConsistentHash;

namespace Uragano.Caching.Redis
{
    public class RedisPartitionCaching : ICaching
    {
        private IDistributedCache Cache { get; }

        private ICodec Codec { get; }

        private IConsistentHash<RedisConnection> ConsistentHash { get; }

        public RedisPartitionCaching(UraganoSettings uraganoSettings, IServiceProvider serviceProvider, ICodec codec, IConsistentHash<RedisConnection> consistentHash)
        {
            Codec = codec;
            var redisOptions = (RedisOptions)uraganoSettings.CachingOptions;
            ConsistentHash = consistentHash;
            var policy = serviceProvider.GetService<Func<string, IEnumerable<RedisConnection>, RedisConnection>>();
            if (policy == null)
            {
                foreach (var item in redisOptions.ConnectionStrings)
                {
                    ConsistentHash.AddNode(item, item.ToString());
                }
                policy = (key, connections) => ConsistentHash.GetNodeForKey(key);
            }

            string NodeRule(string key)
            {
                var connection = policy(key, redisOptions.ConnectionStrings);
                return $"{connection.Host}:{connection.Port}/{connection.DefaultDatabase}";
            }

            RedisHelper.Initialization(new CSRedis.CSRedisClient(NodeRule, redisOptions.ConnectionStrings.Select(p => p.ToString()).ToArray()));
            Cache = new Microsoft.Extensions.Caching.Redis.CSRedisCache(RedisHelper.Instance);
        }

        public async Task Set<TValue>(string key, TValue value, int expireSeconds = -1)
        {
            if (expireSeconds > 0)
                await Cache.SetAsync(key, Codec.Serialize(value), new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expireSeconds <= 0 ? default : TimeSpan.FromSeconds(expireSeconds)
                });
            else
                await Cache.SetAsync(key, Codec.Serialize(value));
        }

        public async Task<(object value, bool hasKey)> Get(string key, Type type)
        {
            var bytes = await Cache.GetAsync(key);
            if (bytes == null || bytes.LongLength == 0)
                return (null, false);
            return (Codec.Deserialize(bytes, type), true);
        }

        public async Task<(TValue value, bool hasKey)> Get<TValue>(string key)
        {
            var (value, hasKey) = await Get(key, typeof(TValue));
            return (value == null ? default : (TValue)value, hasKey);
        }

        public async Task Remove(params string[] keys)
        {
            await Cache.RemoveAsync(string.Join("|", keys));
        }
    }
}
