using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace VsTestkit.Testing
{
    /// <summary>
    /// Server-side world manipulation and inspection.
    ///
    /// Every method here runs on the server main thread and touches live objects
    /// directly, which is safe precisely because test bodies never leave that
    /// thread except at an await.
    /// </summary>
    public static class World
    {
        // ---------- blocks ----------

        public static Block Block(string code)
        {
            var sapi = Vs.RequireServer();
            var loc = ToLocation(code);
            var block = sapi.World.GetBlock(loc);
            if (block == null) throw new AssertionException($"no such block: {code}");
            return block;
        }

        public static void SetBlock(string code, BlockPos pos)
        {
            var sapi = Vs.RequireServer();
            sapi.World.BlockAccessor.SetBlock(Block(code).BlockId, pos);
        }

        public static Block GetBlock(BlockPos pos) =>
            Vs.RequireServer().World.BlockAccessor.GetBlock(pos);

        /// <summary>Block code at a position, as a string - the usual thing to assert on.</summary>
        public static string BlockCode(BlockPos pos) => GetBlock(pos)?.Code?.ToString();

        public static void Fill(BlockPos from, BlockPos to, string code)
        {
            var sapi = Vs.RequireServer();
            var id = code == null ? 0 : Block(code).BlockId;
            var acc = sapi.World.GetBlockAccessorBulkUpdate(true, true);

            var (x0, x1) = Order(from.X, to.X);
            var (y0, y1) = Order(from.Y, to.Y);
            var (z0, z1) = Order(from.Z, to.Z);

            var p = new BlockPos(from.dimension);
            for (var x = x0; x <= x1; x++)
                for (var y = y0; y <= y1; y++)
                    for (var z = z0; z <= z1; z++)
                    {
                        p.Set(x, y, z);
                        acc.SetBlock(id, p);
                    }

            acc.Commit();
        }

        /// <summary>Top solid block height at a column.</summary>
        public static int GroundY(int x, int z) =>
            Vs.RequireServer().World.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, 0));

        // ---------- block entities ----------

        public static T BE<T>(BlockPos pos) where T : BlockEntity
        {
            var sapi = Vs.RequireServer();
            var be = sapi.World.BlockAccessor.GetBlockEntity(pos);

            if (be == null)
                throw new AssertionException(
                    $"no block entity at {Show(pos)} (block there is {BlockCode(pos) ?? "nothing"})");

            if (be is T typed) return typed;

            throw new AssertionException(
                $"block entity at {Show(pos)} is {be.GetType().Name}, expected {typeof(T).Name}");
        }

        /// <summary>Null instead of throwing, for "is it there yet" checks.</summary>
        public static T BEOrNull<T>(BlockPos pos) where T : BlockEntity =>
            Vs.RequireServer().World.BlockAccessor.GetBlockEntity(pos) as T;

        // ---------- entities ----------

        public static Entity SpawnEntity(string code, BlockPos at)
        {
            var sapi = Vs.RequireServer();
            var type = sapi.World.GetEntityType(ToLocation(code));
            if (type == null) throw new AssertionException($"no such entity type: {code}");

            var entity = sapi.World.ClassRegistry.CreateEntity(type);
            entity.Pos.SetPos(at.ToVec3d().Add(0.5, 0, 0.5));
            sapi.World.SpawnEntity(entity);
            return entity;
        }

        public static Entity[] Entities(BlockPos center, float radius) =>
            Vs.RequireServer().World.GetEntitiesAround(
                center.ToVec3d().Add(0.5, 0.5, 0.5), radius, radius) ?? new Entity[0];

        public static Entity[] Entities(BlockPos center, float radius, string codeContains) =>
            Entities(center, radius)
                .Where(e => e.Code?.ToString().Contains(codeContains) == true)
                .ToArray();

        // ---------- items ----------

        public static ItemStack Stack(string code, int quantity = 1)
        {
            var sapi = Vs.RequireServer();
            var loc = ToLocation(code);

            var item = sapi.World.GetItem(loc);
            if (item != null) return new ItemStack(item, quantity);

            var block = sapi.World.GetBlock(loc);
            if (block != null) return new ItemStack(block, quantity);

            throw new AssertionException($"no such item or block: {code}");
        }

        // ---------- chunks ----------

        /// <summary>
        /// Force a region of chunk columns into memory and wait for it.
        ///
        /// Writing a block into an unloaded chunk succeeds silently and reads back
        /// as air, so anything that builds must load first.
        /// </summary>
        public static Task LoadArea(BlockPos min, BlockPos max, bool keepLoaded = true, int chunkMargin = 1)
        {
            var sapi = Vs.RequireServer();
            var size = sapi.WorldManager.ChunkSize;

            // The margin is not padding for its own sake. A great deal of mod
            // code - and vanilla farmland, which is where the idiom comes from -
            // guards its tick with IsFullyLoadedChunk, and that is
            // ServerChunk.NotAtEdge, which requires NeighboursLoaded == 511:
            // all eight surrounding chunk columns present. Load only the columns
            // the plot sits in and such code never runs at all, silently, and the
            // test reads as "the mod does nothing".
            var cx1 = Math.Min(min.X, max.X) / size - chunkMargin;
            var cx2 = Math.Max(min.X, max.X) / size + chunkMargin;
            var cz1 = Math.Min(min.Z, max.Z) / size - chunkMargin;
            var cz2 = Math.Max(min.Z, max.Z) / size + chunkMargin;

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            sapi.WorldManager.LoadChunkColumnPriority(cx1, cz1, cx2, cz2, new Vintagestory.API.Server.ChunkLoadOptions
            {
                KeepLoaded = keepLoaded,
                OnLoaded = () => tcs.TrySetResult(null)
            });
            return tcs.Task;
        }

        // ---------- weather ----------

        /// <summary>
        /// Forces precipitation to a fixed value, or null to hand it back to the
        /// weather simulation.
        ///
        /// The harness pins this to 0 at startup, because sky-exposed farmland
        /// accumulates every hour of rain since its last update and would
        /// otherwise moisten on its own whenever a test advances the calendar.
        /// Set it deliberately when rain is what you are testing.
        /// </summary>
        public static void SetPrecipitation(float? level)
        {
            var sapi = Vs.RequireServer();
            var weather = sapi.ModLoader.GetModSystem<Vintagestory.GameContent.WeatherSystemServer>();
            if (weather == null) throw new AssertionException("no weather system is loaded");
            weather.OverridePrecipitation = level;
        }

        // ---------- helpers ----------

        internal static AssetLocation ToLocation(string code)
        {
            // Bare codes are overwhelmingly vanilla, and "game:" on every literal
            // makes tests noisier than they need to be.
            return code.Contains(":") ? new AssetLocation(code) : new AssetLocation("game", code);
        }

        static (int, int) Order(int a, int b) => a <= b ? (a, b) : (b, a);

        static string Show(BlockPos p) => $"{p.X},{p.Y},{p.Z}";
    }
}
