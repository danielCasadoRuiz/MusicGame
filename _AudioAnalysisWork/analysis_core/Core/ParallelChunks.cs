using System;
using System.Threading;
using System.Threading.Tasks;

namespace MusicGame.Audio.Analysis
{
    /// <summary>Execution options of one analysis run (not part of the results → not hashed).</summary>
    public sealed class AnalysisRunOptions
    {
        /// <summary>Max worker threads. ≤ 1 = run sequentially on the calling thread (same chunking,
        /// bitwise-identical output). Default: half the cores, capped at 4 (mobile-friendly).</summary>
        public int maxWorkers = DefaultWorkers();
        /// <summary>Frames per work item. Big enough to amortise scheduling, small enough to balance.</summary>
        public int chunkFrames = 1024;
        public CancellationToken cancellation;
        /// <summary>0..1 progress callback. Invoked from WORKER threads — marshal to the main thread yourself.</summary>
        public Action<float> progress;

        public static int DefaultWorkers() => Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
    }

    /// <summary>
    /// Bounded-concurrency chunk runner: each worker gets its own scratch object (created once per
    /// worker, never shared). Chunks write disjoint output ranges only, so the result does not
    /// depend on scheduling.
    /// </summary>
    public static class ParallelChunks
    {
        public static void Run<TScratch>(int itemCount, int chunkSize, AnalysisRunOptions opt,
                                         Func<TScratch> createScratch, Action<int, int, TScratch> body,
                                         Action<int> onChunkDone = null)
        {
            if (itemCount <= 0) return;
            chunkSize = Math.Max(1, chunkSize);
            int chunks = (itemCount + chunkSize - 1) / chunkSize;
            var ct = opt?.cancellation ?? CancellationToken.None;
            int workers = opt?.maxWorkers ?? 1;

            if (workers <= 1 || chunks == 1)
            {
                var s = createScratch();
                for (int c = 0; c < chunks; c++)
                {
                    ct.ThrowIfCancellationRequested();
                    int a = c * chunkSize, b = Math.Min(itemCount, a + chunkSize);
                    body(a, b, s);
                    onChunkDone?.Invoke(c);
                }
                return;
            }

            var po = new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = ct };
            Parallel.For(0, chunks, po, createScratch, (c, state, s) =>
            {
                if (ct.IsCancellationRequested) { state.Stop(); return s; }
                int a = c * chunkSize, b = Math.Min(itemCount, a + chunkSize);
                body(a, b, s);
                onChunkDone?.Invoke(c);
                return s;
            }, s => { });
            ct.ThrowIfCancellationRequested();
        }
    }
}
