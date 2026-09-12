using System.Collections.Generic;
using KpopManager.Core.Systems.Chart;

namespace KpopManager.Core.Systems.Generation
{
    // Builds the entire starting world in one deterministic pass: the company and its three
    // centers, the player's one underperforming group and twelve trainees, two sibling rival
    // centers, and (Phase 3b: grown from ~15) a config-driven pyramid of groups scattered across
    // 25 other companies to give the industry some history.
    //
    // Call once, immediately after a fresh GameState is created and before any ticking — every
    // draw comes from GameState.Random in a fixed order, so the same seed always reproduces the
    // same world, but only from that exact starting point.
    public static class WorldGenerator
    {
        private const int TraineeCount = 12;

        // DESIGN: placeholder names — nothing in the Phase 2 brief asks for a naming scheme for
        // the company or its centers, so these exist purely so the harness has something readable
        // to print. Revisit whenever the game wants the player to name their own center.
        private const string CompanyName = "DreamNova Entertainment";
        private const string PlayerCenterName = "Aurora Center";
        private static readonly string[] RivalCenterNames = { "Solstice Center", "Velvet Sound Center" };

        // Phase 3b fix 2a: 25 companies rather than 5, so a 200-group world doesn't split evenly
        // five ways (the Industry table would otherwise show the same handful of company names on
        // nearly every row).
        private static readonly string[] WorldCompanyNames =
        {
            "Neon Muse Entertainment", "Halcyon Group", "Crimson Wave Entertainment",
            "Orbit Media", "Silver Arc Entertainment", "Lunar Tide Entertainment",
            "Paper Crane Media", "Ivory Coast Entertainment", "Rosewood Group",
            "Static Bloom Entertainment", "Echo Park Media", "Wildfire Entertainment",
            "Glasswing Group", "Moonchild Media", "Aurora Line Entertainment",
            "Firefly Club Entertainment", "Second Skin Media", "Chrome Heart Group",
            "Kaleidoscope Entertainment", "Afterglow Media", "Reverie Group",
            "Trinity Code Entertainment", "Zero Gravity Media", "Skyline Six Entertainment",
            "Blue Hour Group"
        };

        // Builds the world into state, attaching data as its content.
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

        // DESIGN: Rookie or Rising tier, debuted 1-2 years ago, then deliberately depressed below
        // what that tier roll would normally produce — per DESIGN.md's "you get hired" framing,
        // the player inherits a real problem, not a blank slate. Revisit during balancing once
        // Phase 3's chart sim can validate "underperforming" against real chart outcomes.
        private static void GeneratePlayerGroup(GameState state, SimRandom rng, SimDate now, ProductionCenter player)
        {
            int tier = rng.Chance(0.5f) ? 0 : 1; // Rookie or Rising
            SimDate debut = now.AdvanceWeeks(-rng.NextInt(52, 105)); // 1–2 years ago

            Group group = GroupGenerator.Generate(state, player.Id, tier, debut);

            group.Fandom.Size = (long)(group.Fandom.Size * 0.4f);
            group.Fandom.Sentiment = Clamp(group.Fandom.Sentiment * 0.7f, 0f, 100f);

            player.GroupIds.Add(group.Id);
        }

        // DESIGN: mostly average with an occasional standout, so the eventual debut decision
        // (Phase 5) has real stakes rather than an obvious pick. Revisit during balancing.
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

        // The two sibling centers in the same building — 2-3 groups each, none at Legendary tier.
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

        // Phase 3b fix 2a: ChartConfig.WorldGroupCount groups (200, up from a hardcoded 15 —
        // mean active releases per week was 19.5 against ChartSize=100, so the chart was never a
        // fifth full and several metrics were passing against no real scarcity), tier-pyramided
        // by ChartConfig.WorldTierShareRookie etc., and staggered up to
        // ChartConfig.WorldGroupDebutMaxYearsAgo years back so the industry reads as having
        // history rather than starting from nothing. Spread round-robin across every entry in
        // WorldCompanyNames.
        private static void GenerateWorldGroups(GameState state, SimRandom rng, SimDate now)
        {
            ChartConfig config = state.ChartConfig;

            List<ProductionCenter> worldCenters = new List<ProductionCenter>(WorldCompanyNames.Length);
            for (int i = 0; i < WorldCompanyNames.Length; i++)
            {
                worldCenters.Add(CreateCenter(state, WorldCompanyNames[i], isPlayer: false, CenterTier.Established));
            }

            int[] tierCounts = TierCountsFromShares(config);

            int centerCursor = 0;
            for (int tier = 0; tier < tierCounts.Length; tier++)
            {
                for (int i = 0; i < tierCounts[tier]; i++)
                {
                    SimDate debut = now.AdvanceWeeks(-rng.NextInt(13, config.WorldGroupDebutMaxYearsAgo * SimDate.WeeksPerYear + 1));

                    ProductionCenter home = worldCenters[centerCursor % worldCenters.Count];
                    centerCursor++;

                    Group group = GroupGenerator.Generate(state, home.Id, tier, debut);
                    home.GroupIds.Add(group.Id);
                }
            }
        }

        // Converts the five tier-share fractions into whole-group counts summing to
        // ChartConfig.WorldGroupCount, with any rounding remainder dropped into Rookie (the
        // floor tier, and the one large enough that a few extra or fewer groups there doesn't
        // shift the shape of the pyramid).
        private static int[] TierCountsFromShares(ChartConfig config)
        {
            int total = config.WorldGroupCount;

            int[] counts = new int[5];
            counts[(int)GroupTier.Rising] = (int)(total * config.WorldTierShareRising);
            counts[(int)GroupTier.Established] = (int)(total * config.WorldTierShareEstablished);
            counts[(int)GroupTier.TopTier] = (int)(total * config.WorldTierShareTopTier);
            counts[(int)GroupTier.Legendary] = (int)(total * config.WorldTierShareLegendary);

            int assigned = counts[(int)GroupTier.Rising] + counts[(int)GroupTier.Established] +
                counts[(int)GroupTier.TopTier] + counts[(int)GroupTier.Legendary];
            counts[(int)GroupTier.Rookie] = System.Math.Max(0, total - assigned);

            return counts;
        }

        private static float Clamp(float value, float min, float max)
        {
            return value < min ? min : (value > max ? max : value);
        }
    }
}
