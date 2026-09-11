using System;
using System.Collections.Generic;

namespace KpopManager.Core
{
    /// <summary>
    /// A snapshot of a <see cref="SimRandom"/> instance's complete internal state.
    /// Everything needed to resume the generator bit-for-bit lives here, including the
    /// cached Gaussian spare, which is easy to forget and which silently breaks determinism
    /// across a save/load boundary if it is dropped.
    /// </summary>
    public struct SimRandomState
    {
        public ulong State { get; set; }
        public ulong Inc { get; set; }
        public bool HasGaussianSpare { get; set; }
        public float GaussianSpare { get; set; }
    }

    /// <summary>
    /// The simulation's only source of randomness: PCG32 (XSH RR 64/32), implemented by hand.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Never use <c>System.Random</c> (its internals are not guaranteed stable across runtime
    /// versions) or <c>UnityEngine.Random</c> (global mutable state, not serialisable).
    /// Same seed plus same inputs must reproduce an identical 50-year history.
    /// </para>
    /// <para>
    /// One seeded instance lives on <see cref="GameState"/>. Systems draw from that instance
    /// in tick order; because the order is fixed, the draw sequence is fixed.
    /// </para>
    /// </remarks>
    public sealed class SimRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private ulong _inc;

        private bool _hasGaussianSpare;
        private float _gaussianSpare;

        /// <summary>Creates a generator seeded with <paramref name="seed"/> on stream <paramref name="sequence"/>.</summary>
        /// <param name="seed">The seed. The whole run is reproducible from this value.</param>
        /// <param name="sequence">
        /// Stream selector. Two generators with the same seed but different sequences produce
        /// unrelated streams. Defaults to 1; only change it if a subsystem ever needs its own
        /// independent stream (it should not, because one shared generator keeps the draw order
        /// auditable).
        /// </param>
        public SimRandom(ulong seed, ulong sequence = 1UL)
        {
            Seed(seed, sequence);
        }

        /// <summary>Creates a generator restored from a previously captured state.</summary>
        public SimRandom(SimRandomState state)
        {
            SetState(state);
        }

        /// <summary>PCG internal state word. Public with a setter so Phase 9 save/load can restore it.</summary>
        public ulong State
        {
            get => _state;
            set => _state = value;
        }

        /// <summary>PCG stream increment (always odd). Public with a setter for save/load.</summary>
        public ulong Inc
        {
            get => _inc;
            set => _inc = value;
        }

        /// <summary>Whether a Marsaglia spare value is waiting to be returned by the next Gaussian draw.</summary>
        public bool HasGaussianSpare
        {
            get => _hasGaussianSpare;
            set => _hasGaussianSpare = value;
        }

        /// <summary>The cached Marsaglia spare. Meaningless unless <see cref="HasGaussianSpare"/> is true.</summary>
        public float GaussianSpare
        {
            get => _gaussianSpare;
            set => _gaussianSpare = value;
        }

        /// <summary>Re-seeds the generator in place, discarding any cached Gaussian spare.</summary>
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

        /// <summary>Captures the complete generator state.</summary>
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

        /// <summary>Restores a previously captured state. Subsequent draws then repeat exactly.</summary>
        public void SetState(SimRandomState state)
        {
            _state = state.State;
            // An even increment would collapse the period of the generator, so force the low bit
            // in case a hand-edited or corrupted save supplies one.
            _inc = state.Inc | 1UL;
            _hasGaussianSpare = state.HasGaussianSpare;
            _gaussianSpare = state.GaussianSpare;
        }

        /// <summary>Draws the next raw 32-bit value. Every other method is built on this one.</summary>
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

        /// <summary>Draws a value in <c>[minInclusive, maxExclusive)</c> with no modulo bias.</summary>
        /// <exception cref="ArgumentOutOfRangeException">If the range is empty.</exception>
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

        /// <summary>Draws a value in <c>[0, maxExclusive)</c>.</summary>
        public int NextInt(int maxExclusive) => NextInt(0, maxExclusive);

        /// <summary>Draws a float in <c>[0, 1)</c>.</summary>
        public float NextFloat()
        {
            // 24 bits is exactly the float mantissa width, so every result is exactly
            // representable and the distribution is uniform over the 2^24 available values.
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        /// <summary>Draws a float in <c>[min, max)</c>.</summary>
        public float NextFloat(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>
        /// Draws from a normal distribution using the Marsaglia polar method, caching the spare
        /// value that the method produces for free.
        /// </summary>
        /// <remarks>
        /// DESIGN: chose the polar method over Box-Muller because it needs no trig, and trig is
        /// the least portable part of the standard library. It still uses <see cref="Math.Log"/>,
        /// which .NET does not guarantee to be bit-identical across runtime versions or CPUs
        /// (<see cref="Math.Sqrt"/> is IEEE-754 exact, so that half is safe). PC-only and a single
        /// target machine means this is fine today; if determinism ever has to hold across
        /// machines, swap Log for a fixed polynomial approximation. Revisit during balancing.
        /// </remarks>
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

        /// <summary>Draws a standard normal value (mean 0, standard deviation 1).</summary>
        public float NextGaussian() => NextGaussian(0f, 1f);

        /// <summary>Returns true with the given probability. Values outside [0,1] are clamped.</summary>
        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return NextFloat() < probability;
        }

        /// <summary>Picks one item uniformly at random.</summary>
        /// <exception cref="ArgumentException">If the list is empty.</exception>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(items));

            return items[NextInt(0, items.Count)];
        }

        /// <summary>Shuffles in place using Fisher-Yates.</summary>
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
