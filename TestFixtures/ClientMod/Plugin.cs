// Fixture assembly: mimics a BepInEx plugin so the identity scanner can be tested end to end.
// The attribute and interface are declared locally because the scanner matches them by name.
using System;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPluginAttribute : Attribute
    {
        public BepInPluginAttribute(string guid, string name, string version)
        {
            Guid = guid;
            Name = name;
            Version = version;
        }

        public string Guid { get; }

        public string Name { get; }

        public string Version { get; }
    }
}

namespace Fixtures.ClientMod
{
    [BepInEx.BepInPlugin("com.fixture.clientmod", "Fixture Client Mod", "1.4.2")]
    public sealed class Plugin
    {
        public string Describe()
        {
            return "fixture";
        }
    }
}
