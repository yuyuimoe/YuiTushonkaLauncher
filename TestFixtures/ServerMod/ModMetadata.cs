// Fixture assembly: mimics an SPT 4.x server mod, whose identity lives in property initialisers and
// the constructor (not in an attribute), which is what the scanner reads out of the IL.
using System.Collections.Generic;

namespace SPTarkov.Server.Core.Models.Spt.Mod
{
    public interface IModMetadata
    {
        string ModGuid { get; }

        string Name { get; }

        string Author { get; }

        string Version { get; }

        List<string>? Contributors { get; }

        string License { get; }
    }
}

namespace Fixtures.ServerMod
{
    public sealed class ModMetadata : SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata
    {
        public string ModGuid { get; init; } = "com.fixture.servermod";

        public string Name { get; init; } = "Fixture Server Mod";

        public string Author { get; init; } = "fixture-author";

        public string Version { get; init; } = "2.0.1";

        public List<string>? Contributors { get; init; }

        public string License { get; init; } = "MIT";
    }
}
