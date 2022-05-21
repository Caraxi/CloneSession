using Dalamud.Configuration;
using Dalamud.Plugin;
using System;

namespace CloneSession
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public string UserPath = string.Empty;
        public string DalamudPath = string.Empty;
        public string GamePath = string.Empty;
        public string[] DalamudStreams = { "release", "stg", "net5" };
        public string SelectedDalamudStream = "release";

        public bool AutoClone = false;

        public bool Sandboxie = false;
        public string SandboxiePath = @"C:\Program Files\Sandboxie-Plus\Start.exe";
        public string SandboxieBox = string.Empty;
    }
}
