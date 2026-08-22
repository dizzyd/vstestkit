// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Dave (Dizzy) Smith
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace VsTestkit
{
    /// <summary>
    /// Universal mod system. In singleplayer the client-side and server-side mod
    /// loaders resolve the same assembly (ModAssemblyLoader uses
    /// Assembly.UnsafeLoadFrom into the default ALC), so both instances share
    /// <see cref="Hub"/> statics and one endpoint can reach both sides.
    /// </summary>
    public class TestkitModSystem : ModSystem
    {
        public override double ExecuteOrder() => 0.01;

        // Loading on both sides is the whole point.
        public override bool ShouldLoad(EnumAppSide side) => true;

        public override void StartServerSide(ICoreServerAPI api)
        {
            Hub.AttachServer(api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            Hub.AttachClient(api);
        }

        public override void Dispose()
        {
            Hub.Detach(Mod?.Info?.ModID);
        }
    }
}
