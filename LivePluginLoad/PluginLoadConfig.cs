using Newtonsoft.Json;

namespace LivePluginLoad {
    public class PluginLoadConfig {
        public string FilePath { get; set; }
        public bool LoadAtStartup { get; set; }
        public bool AutoReload { get; set; }

        [JsonIgnore]
        public bool Loaded { get; set; }

        [JsonIgnore]
        public long FileSize { get; set; }
        
        [JsonIgnore]
        public long FileChanged { get; set; }

        [JsonIgnore]
        public string PluginInternalName { get; set; }

        [JsonIgnore]
        public bool PerformReload { get; internal set; }

        [JsonIgnore]
        public bool PerformLoad { get; internal set; }

        [JsonIgnore]
        public bool PerformUnload { get; internal set; }
        

    }
}
