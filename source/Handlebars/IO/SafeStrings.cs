using System.Runtime.CompilerServices;
using System.Threading;

namespace HandlebarsDotNet.IO
{
    /// <summary>
    /// Tracks which <see cref="string"/> instances already went through the encoding pipeline
    /// (e.g. captured output of a subexpression helper) so they are not encoded a second time
    /// when written elsewhere. Marking is by object reference, not value, so it never affects
    /// any string a caller didn't obtain from this exact pipeline — and critically, the marked
    /// value stays a plain <see cref="string"/> the whole way through, so it round-trips safely
    /// through helper argument binding, reflection-based helpers, and any other consumer that
    /// only knows how to handle <see cref="string"/>.
    /// </summary>
    internal static class SafeStrings
    {
        private static readonly ConditionalWeakTable<string, object> Marked = new();
        private static readonly object Sentinel = new();

        // Marking only ever happens when a helper's captured output is written (ReturnInvoke).
        // Most applications never mark a single string, yet IsSafe sits on the hot path of every
        // string written to output — so keep a global "has anything ever been marked" latch to
        // skip the ConditionalWeakTable probe entirely until the first Mark. The latch is written
        // with release semantics after the table entry exists, so a true reader always observes
        // the corresponding table entry; a stale false reader merely re-encodes on the same
        // thread-interleaving that was already possible before the mark completed.
        private static bool _anyMarked;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Mark(string value)
        {
            if (value.Length == 0) return value;
            Marked.GetValue(value, _ => Sentinel);
            Volatile.Write(ref _anyMarked, true);
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsSafe(string value)
            => value.Length == 0 || (Volatile.Read(ref _anyMarked) && Marked.TryGetValue(value, out _));
    }
}
