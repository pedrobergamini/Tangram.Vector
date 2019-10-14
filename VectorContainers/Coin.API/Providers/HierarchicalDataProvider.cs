﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Core.API.Consensus;
using Core.API.Helper;
using Core.API.Model;
using Microsoft.Extensions.Logging;

namespace Coin.API.Providers
{
    public class HierarchicalDataProvider
    {
        private static readonly AsyncLock markAsMutex = new AsyncLock();

        public ConcurrentQueue<BlockGraphProto> DataQueue { get; private set; }

        private readonly IUnitOfWork unitOfWork;
        private readonly ILogger logger;

        public HierarchicalDataProvider(IUnitOfWork unitOfWork, ILogger<HierarchicalDataProvider> logger)
        {
            this.unitOfWork = unitOfWork;
            this.logger = logger;

            DataQueue = new ConcurrentQueue<BlockGraphProto>();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task Run(CancellationToken cancellationToken)
        {
            try
            {
                var jobs = await unitOfWork.Job.GetStatusMany(JobState.Blockmainia);
                if (jobs.Any() != true)
                {
                    return;
                }

                foreach (var job in jobs)
                {
                    DataQueue.Enqueue(job.BlockGraph);
                    await unitOfWork.Job.SetStatus(job, JobState.Running);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"<<< HierarchicalDataProvider.Run >>>: {ex.ToString()}");
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="lookup"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        public static IEnumerable<BlockGraphProto> NextBlockGraph(ILookup<string, BlockGraphProto> lookup, ulong node)
        {
            if (lookup == null)
                throw new ArgumentNullException(nameof(lookup));

            if (node < 0)
                throw new ArgumentOutOfRangeException(nameof(node));

            for (int i = 0, lookupCount = lookup.Count; i < lookupCount; i++)
            {
                var blockGraphs = lookup.ElementAt(i);
                BlockGraphProto root = null;

                var sorted = CurrentNodeFirst(blockGraphs.ToList(), node);

                foreach (var next in sorted)
                {
                    if (next.Block.Node.Equals(node))
                        root = NewBlockGraph(next);
                    else
                        AddDependency(root, next);
                }

                if (root == null)
                    continue;

                yield return root;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockGraphs"></param>
        /// <param name="node"></param>
        /// <returns></returns>
        private static IEnumerable<BlockGraphProto> CurrentNodeFirst(List<BlockGraphProto> blockGraphs, ulong node)
        {
            // Not the best solution...
            var list = new List<BlockGraphProto>();
            var nodeIndex = blockGraphs.FindIndex(x => x.Block.Node.Equals(node));

            list.Add(blockGraphs[nodeIndex]);
            blockGraphs.RemoveAt(nodeIndex);
            list.AddRange(blockGraphs);

            return list;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="next"></param>
        /// <returns></returns>
        private static BlockGraphProto NewBlockGraph(BlockGraphProto next)
        {
            if (next == null)
                throw new ArgumentNullException(nameof(next));

            return new BlockGraphProto
            {
                Block = next.Block,
                Deps = next.Deps,
                Id = next.Id,
                Prev = next.Prev,
                Included = next.Included,
                Replied = next.Replied
            };
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="root"></param>
        /// <param name="next"></param>
        public static void AddDependency(BlockGraphProto root, BlockGraphProto next)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            if (next == null)
                throw new ArgumentNullException(nameof(next));

            if (root.Deps?.Any() != true)
            {
                root.Deps = new List<DepProto>();
            }

            root.Deps.Add(new DepProto
            {
                Id = next.Id,
                Block = next.Block,
                Deps = next.Deps?.Select(d => d.Block).ToList(),
                Prev = next.Prev ?? null
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="blockIDs"></param>
        /// <returns></returns>
        public async Task MarkAs(IEnumerable<BlockID> blockIDs, JobState state)
        {
            if (blockIDs == null)
                throw new ArgumentNullException(nameof(blockIDs));

            using (await markAsMutex.LockAsync())
            {
                try
                {
                    if (blockIDs.Any() != true)
                        return;

                    foreach (var next in blockIDs)
                    {
                        var jobProto = await unitOfWork.Job.Get(next.Hash);
                        if (jobProto != null)
                        {
                            jobProto.Status = state;

                            var saved = await unitOfWork.Job.StoreOrUpdate(jobProto, jobProto.Id);
                            if (saved == null)
                                throw new Exception($"Could not save job {jobProto.Id} for block {next.Node} and round {next.Round}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError($"<<< HierarchicalDataProvider.MarkAs >>>: {ex.ToString()}");
                }
            }
        }
    }
}
