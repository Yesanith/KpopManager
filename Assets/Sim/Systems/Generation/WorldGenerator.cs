using System.Collections.Generic;

namespace KpopManager.Core.Systems.Generation
{
    /// <summary>
    /// Builds the entire starting world in one deterministic pass: the company and its three
    /// centers, the player's one underperforming group and twelve trainees, two sibling rival
    /// centers, and roughly fifteen groups scattered across five other companies to give the
    /// industry some history.
    /// </summary>
    /// <remarks>
    /// Call once, immediately after a fresh <see cref="GameState"/> is created and before any
    /// ticking — every draw comes from <see cref="GameState.Random"/> in a fixed order, so the
    /// same seed always reproduces the same world, but only from that exact starting point.
    /// </remarks>
    public static class WorldGenerator
    {
        private const int TraineeCount = 12;

        // DESIGN: placeholder names — nothing in the Phase 2 brief asks for a naming scheme for
        // the company or its centers, so these exist purely so the harness has something readable
        // to print. Revisit whenever the game wants the player to name their own center.
        private const string CompanyName = "DreamNova Entertainment";
        private const string PlayerCenterName = "Aurora Center";
        private static readonly string[] RivalCenterNames = { "Solstice Center", "Velvet Sound Center" };
        private static readonly string[] WorldCompanyNames =
        {
            "Neon Muse Entertainment", "Halcyon Group", "Crimson Wave Entertainment",
            "Orbit Media", "Silver Arc Entertainment"
        };

        /// <summary>Builds the world into <paramref name="state"/>, attaching <paramref name="data"/> as its content.</summary>
        public static void Generate(GameState state, WorldData data)
        {
            state.WorldData = data;

            SimRandom rng = state.Random;
            SimDate now = state.Date;

            Company company = new Company { Name = CompanyName };
            state.Company = company;

            ProductionCenter player = CreateCenter(state, PlayerCenterName, isPlayer: true, CenterTier.Junior);
            company.CenterIds.Add(player.Id);

            List<ProductionCenter> rivals = new List<ProductionCenter>(RivalCenterNames.Length);
            for (int i = 0; i < RivalCenterNames.Length; i++)
            {
                ProductionCenter rival = CreateCenter(state, RivalCenterNames[i], isPlayer: false, CenterTier.Established);
                company.CenterIds.Add(rival.Id);
                rivals.Add(rival);
            }

            GeneratePlayerGroup(state, rng, now, player);
            GenerateTrainees(state, rng, now, player, data);
            GenerateRivalGroups(state, rng, now, rivals);
            GenerateWorldGroups(state, rng, now);
        }

        private static ProductionCenter CreateCenter(GameState state, string name, bool isPlayer, CenterTier tier)
        {
            ProductionCenter center = new ProductionCenter
            {
                Id = state.AllocateEntityId(),
                Name = name,
                IsPlayer = isPlayer,
                Budget = 500_000f,
                Goodwill = 50f,
                Tier = tier
            };
            state.AddCenter(center);
            return center;
        }

        /// <summary>
        /// DESIGN: Rookie or Rising tier, debuted 1–2 years ago, then deliberately depressed below
        /// what that tier roll would normally produce — per DESIGN.md's "you get hired" framing,
        /// the player inherits a real problem, not a blank slate. Revisit during balancing once
        /// Phase 3's chart sim can validate "underperforming" against real chart outcomes.
        /// </summary>
        private static void GeneratePlayerGroup(GameState state, SimRandom rng, SimDate now, ProductionCenter player)
        {
            int tier = rng.Chance(0.5f) ? 0 : 1; // Rookie or Rising
            SimDate debut = now.AdvanceWeeks(-rng.NextInt(52, 105)); // 1–2 years ago

            Group group = GroupGenerator.Generate(state, player.Id, tier, debut);

            group.Fandom.Size = (long)(group.Fandom.Size * 0.4f);
            group.Fandom.Sentiment = Clamp(group.Fandom.Sentiment * 0.7f, 0f, 100f);

            player.GroupIds.Add(group.Id);
        }

        /// <summary>DESIGN: mostly average with an occasional standout, so the eventual debut
        /// decision (Phase 5) has real stakes rather than an obvious pick. Revisit during balancing.</summary>
        private static void GenerateTrainees(GameState state, SimRandom rng, SimDate now, ProductionCenter player, WorldData data)
        {
            for (int i = 0; i < TraineeCount; i++)
            {
                int age = 15 + rng.NextInt(0, 5); // 15–19
                int birthYear = now.Year - age;
                int tier = RollTraineeTier(rng);

                Person trainee = PersonGenerator.Generate(rng, birthYear, tier, PersonStatus.Trainee, now, null, null, data);
                trainee.Id = state.AllocateEntityId();
                trainee.CenterId = player.Id;

                state.AddPerson(trainee);
                player.TraineeIds.Add(trainee.Id);
            }
        }

        private static int RollTraineeTier(SimRandom rng)
        {
            float r = rng.NextFloat();
            if (r < 0.55f) return 0;
            if (r < 0.85f) return 1;
            if (r < 0.97f) return 2;
            return 3;
        }

        /// <summary>The two sibling centers in the same building — 2–3 groups each, none at Legendary tier.</summary>
        private static void GenerateRivalGroups(GameState state, SimRandom rng, SimDate now, List<ProductionCenter> rivals)
        {
            for (int r = 0; r < rivals.Count; r++)
            {
                int groupCount = 2 + rng.NextInt(0, 2); // 2–3
                for (int g = 0; g < groupCount; g++)
                {
                    int tier = rng.NextInt(0, 4); // Rookie..TopTier
                    SimDate debut = now.AdvanceWeeks(-rng.NextInt(26, 6 * SimDate.WeeksPerYear));

                    Group group = GroupGenerator.Generate(state, rivals[r].Id, tier, debut);
                    rivals[r].GroupIds.Add(group.Id);
                }
            }
        }

        /// <summary>
        /// ~15 groups at other companies, tier-pyramided and staggered up to ten years back so the
        /// industry reads as having history rather than starting from nothing. Spread round-robin
        /// across five placeholder companies rather than one, so the Industry table doesn't show
        /// fifteen groups all crediting the same fake company.
        /// </summary>
        private static void GenerateWorldGroups(GameState state, SimRandom rng, SimDate now)
        {
            List<ProductionCenter> worldCenters = new List<ProductionCenter>(WorldCompanyNames.Length);
            for (int i = 0; i < WorldCompanyNames.Length; i++)
            {
                worldCenters.Add(CreateCenter(state, WorldCompanyNames[i], isPlayer: false, CenterTier.Established));
            }

            // Rookie, Rising, Established, TopTier, Legendary — sums to 15, pyramid-shaped.
            int[] tierCounts = { 6, 4, 3, 1, 1 };

            int centerCursor = 0;
            for (int tier = 0; tier < tierCounts.Length; tier++)
            {
                for (int i = 0; i < tierCounts[tier]; i++)
                {
                    SimDate debut = now.AdvanceWeeks(-rng.NextInt(13, 10 * SimDate.WeeksPerYear + 1));

                    ProductionCenter home = worldCenters[centerCursor % worldCenters.Count];
                    centerCursor++;

                    Group group = GroupGenerator.Generate(state, home.Id, tier, debut);
                    home.GroupIds.Add(group.Id);
                }
            }
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
