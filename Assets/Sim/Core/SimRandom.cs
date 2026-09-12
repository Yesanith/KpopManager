using System;
using System.Collections.Generic;

namespace KpopManager.Core
{
    // A snapshot of a SimRandom instance's complete internal state. Everything needed to resume
    // the generator bit-for-bit lives here, including the cached Gaussian spare, which is easy to
    // forget and which silently breaks determinism across a save/load boundary if it is dropped.
    public struct SimRandomState
    {
        public ulong State { get; set; }
        public ulong Inc { get; set; }
        public bool HasGaussianSpare { get; set; }
        public float GaussianSpare { get; set; }
    }

    // The simulation's only source of randomness: PCG32 (XSH RR 64/32), implemented by hand.
    //
    // Never use System.Random (its internals are not guaranteed stable across runtime versions)
    // or UnityEngine.Random (global mutable state, not serialisable). Same seed plus same inputs
    // must reproduce an identical 50-year history.
    //
    // One seeded instance lives on GameState. Systems draw from that instance in tick order;
    // because the order is fixed, the draw sequence is fixed.
    public sealed class SimRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        private bool _hasGaussianSpare;
        private float _gaussianSpare;

        // sequence: stream selector. Two generators with the same seed but different sequences
        // produce unrelated streams. Defaults to 1; only change it if a subsystem ever needs its
        // own independent stream (it should not — one shared generator keeps the draw order
        // auditable).
        public SimRandom(ulong seed, ulong sequence = 1UL)
        {
            Seed(seed, sequence);
        }

        public SimRandom(SimRandomState state)
        {
            SetState(state);
        }

        // PCG internal state word. Public with a setter so Phase 9 save/load can restore it.
        public ulong State
        {
            get => _state;
            set => _state = value;
        }

        // PCG stream increment (always odd). Public with a setter for save/load.
        public ulong Inc
        {
            get => _inc;
            set => _inc = value;
        }

        public bool HasGaussianSpare
        {
            get => _hasGaussianSpare;
            set => _hasGaussianSpare = value;
        }

        // Meaningless unless HasGaussianSpare is true.
        public float GaussianSpare
        {
            get => _gaussianSpare;
            set => _gaussianSpare = value;
        }

        public void Seed(ulong seed, ulong sequence = 1UL)
        {
            unchecked
            {
                _state = 0UL;
                _inc = (sequence << 1) | 1UL;
                NextUInt();
                _state += seed;
                NextUInt();
            }

            _hasGaussianSpare = false;
            _gaussianSpare = 0f;
        }

        public SimRandomState GetState()
        {
            return new SimRandomState
            {
                State = _state,
                Inc = _inc,
                HasGaussianSpare = _hasGaussianSpare,
                GaussianSpare = _gaussianSpare
            };
        }

        public void SetState(SimRandomState state)
        {
            _state = state.State;
            // An even increment would collapse the period of the generator, so force the low bit
            // in case a hand-edited or corrupted save supplies one.
            _inc = state.Inc | 1UL;
            _hasGaussianSpare = state.HasGaussianSpare;
            _gaussianSpare = state.GaussianSpare;
        }

        // Every other method is built on this one.
        public uint NextUInt()
        {
            unchecked
            {
                ulong old = _state;
                _state = old * Multiplier + _inc;

                uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
            }
        }

        // [minInclusive, maxExclusive), no modulo bias. Throws ArgumentOutOfRangeException if the
        // range is empty.
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive),
                    "maxExclusive must be greater than minInclusive.");
            }

            uint range = unchecked((uint)((long)maxExclusive - minInclusive));

            // Rejection sampling: discard the unevenly covered tail below the threshold so that
            // every value in the range is equally likely. Modulo alone would bias the low end.
            uint threshold = (uint)((0x100000000UL - range) % range);

            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                {
                    return unchecked((int)(minInclusive + (long)(r % range)));
                }
            }
        }

        public int NextInt(int maxExclusive) => NextInt(0, maxExclusive);

        public float NextFloat()
        {
            // 24 bits is exactly the float mantissa width, so every result is exactly
            // representable and the distribution is uniform over the 2^24 available values.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public float NextFloat(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        // Marsaglia polar method, caching the spare value the method produces for free.
        //
        // DESIGN: chose the polar method over Box-Muller because it needs no trig, and trig is
        // the least portable part of the standard library. It still uses Math.Log, which .NET
        // does not guarantee to be bit-identical across runtime versions or CPUs (Math.Sqrt is
        // IEEE-754 exact, so that half is safe). PC-only and a single target machine means this
        // is fine today; if determinism ever has to hold across machines, swap Log for a fixed
        // polynomial approximation. Revisit during balancing.
        public float NextGaussian(float mean, float stdDev)
        {
            if (_hasGaussianSpare)
            {
                _hasGaussianSpare = false;
                return mean + stdDev * _gaussianSpare;
            }

            double u, v, s;
            do
            {
                u = NextFloat() * 2.0 - 1.0;
                v = NextFloat() * 2.0 - 1.0;
                s = u * u + v * v;
            }
            while (s >= 1.0 || s == 0.0);

            double multiplier = Math.Sqrt(-2.0 * Math.Log(s) / s);

            _gaussianSpare = (float)(v * multiplier);
            _hasGaussianSpare = true;

            return mean + stdDev * (float)(u * multiplier);
        }

        public float NextGaussian() => NextGaussian(0f, 1f);

        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return NextFloat() < probability;
        }

        // Throws ArgumentException if the list is empty.
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(items));

            return items[NextInt(0, items.Count)];
        }

        public void Shuffle<T>(IList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));

            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = NextInt(0, i + 1);
                if (i == j) continue;

                T temp = items[i];
                items[i] = items[j];
                items[j] = temp;
            }
        }
    }
}
