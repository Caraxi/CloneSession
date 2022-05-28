using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CloneSession
{
    [Serializable]
    public class Configuration
    {
        public List<ProfileConfig> Profiles = new();
        
        public string[] DalamudStreams = { "release", "stg", "net5" };
        public bool FreeMutex = false;
        public int DefaultProfileIndex;

        [JsonIgnore]
        public ProfileConfig DefaultProfile {
            get {
                if (DefaultProfileIndex >= Profiles.Count) DefaultProfileIndex = 0;
                if (Profiles.Count == 0) Profiles.Add(new ProfileConfig() { ProfileName = "Default" });
                return Profiles[DefaultProfileIndex];
            }
        }


        public void Save() {
            Save(this);
        }

        public static void Save(Configuration config) {
            var configFile = new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CloneSession.json"));
            
            File.WriteAllText(configFile.FullName, JsonConvert.SerializeObject((object) config, Formatting.Indented, new JsonSerializerSettings() {
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                TypeNameHandling = TypeNameHandling.None
            }));
        }

        public static Configuration Load() {
            var configFile = new FileInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CloneSession.json"));
            if (!configFile.Exists) return new Configuration();
            return JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(configFile.FullName), new JsonSerializerSettings()
            {
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Simple,
                TypeNameHandling = TypeNameHandling.None
            }) ?? new Configuration();
        }
        
        
    }

    public class ProfileConfig {
        public string ProfileName = string.Empty;
        public string UserPath = string.Empty;
        public string DalamudPath = string.Empty;
        public string GamePath = string.Empty;
        public string SelectedDalamudStream = "release";

        public override bool Equals(object? obj) {
            if (obj is not ProfileConfig pCfg) return false;
            return UserPath == pCfg.UserPath && DalamudPath == pCfg.DalamudPath && GamePath == pCfg.GamePath && SelectedDalamudStream == pCfg.SelectedDalamudStream;
        }
    }
    
}
