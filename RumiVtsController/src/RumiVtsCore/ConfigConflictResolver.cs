namespace RumiVtsController
{
    internal static class ConfigConflictResolver
    {
        /// <summary>
        /// Enforces mutual exclusion between two boolean flags on a config object.
        /// Compares previous state to detect which flag changed last (last-write wins).
        /// Modifies <paramref name="next"/> in place.
        /// </summary>
        public static void ResolveMutualExclusion<T>(
            T previous,
            T next,
            Func<T, bool> getA,
            Action<T, bool> setA,
            Func<T, bool> getB,
            Action<T, bool> setB,
            bool defaultA = true)
        {
            var prevA = getA(previous);
            var prevB = getB(previous);
            var nextA = getA(next);
            var nextB = getB(next);

            if (nextA && nextB)
            {
                // Both true — last-write wins
                if (nextA != prevA)      setB(next, false);               // A just changed, suppress B
                else if (nextB != prevB) setA(next, false);               // B just changed, suppress A
                else                     { setA(next, defaultA); setB(next, !defaultA); } // simultaneous, use default
            }
            else if (!nextA && !nextB)
            {
                // Both false — enforce default
                setA(next, defaultA);
                setB(next, !defaultA);
            }
            // Exactly one true — valid, no action needed
        }
    }
}
