using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    }
}
