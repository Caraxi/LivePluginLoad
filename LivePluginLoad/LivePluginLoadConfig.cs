using Dalamud.Configuration;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace LivePluginLoad {
    public class LivePluginLoadConfig : IPluginConfiguration {
        [NonSerialized] private DalamudPluginInterface pluginInterface;

        [NonSerialized] private LivePluginLoad plugin;

        public int Version { get; set; }

        public List<PluginLoadConfig> PluginLoadConfigs { get; set; } = new List<PluginLoadConfig>();


        public LivePluginLoadConfig() { }

        public void Init(LivePluginLoad plugin, DalamudPluginInterface pluginInterface) {
            this.plugin = plugin;
            this.pluginInterface = pluginInterface;
        }

        public void Save() {
            pluginInterface.SavePluginConfig(this);
        }

        public bool DrawConfigUI() {
            bool drawConfig = true;
            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoCollapse;
            ImGui.Begin($"{plugin.Name} Config", ref drawConfig, windowFlags);

            ImGui.Columns(5);
            ImGui.SetColumnWidth(0, ImGui.GetWindowWidth() - 350);
            ImGui.SetColumnWidth(1, 60);
            ImGui.SetColumnWidth(2, 60);
            ImGui.SetColumnWidth(3, 90);
            ImGui.SetColumnWidth(4, 150);
            ImGui.Text("Plugin Path");
            ImGui.NextColumn();
            ImGui.Text("Load at");
            ImGui.Text("Startup");
            ImGui.NextColumn();
            ImGui.Text("Auto");
            ImGui.Text("Reload");
            ImGui.NextColumn();
            ImGui.Text("Load/");
            ImGui.Text("Unload");
            ImGui.NextColumn();
            ImGui.Text("Remove");
            ImGui.NextColumn();
            ImGui.Separator();

            int index = 0;

            PluginLoadConfig doRemove = null;
            
            foreach (var plc in PluginLoadConfigs) {
                var path = plc.FilePath;
                var loadAtStartup = plc.LoadAtStartup;
                var autoReload = plc.AutoReload;
                var changed = false;
                ImGui.SetNextItemWidth(-1);
                changed = ImGui.InputText($"###inputText_PLC_Path_{index}", ref path, 512);
                ImGui.NextColumn();
                changed = ImGui.Checkbox($"###checkbox_PLC_LoadAtStartup_{index}", ref loadAtStartup) || changed;
                ImGui.NextColumn();
                changed = ImGui.Checkbox($"###checkbox_PLC_AutoReload_{index}", ref autoReload) || changed;
                ImGui.NextColumn();

                if (plc.Loaded) {
                    if (ImGui.Button($"Unload###unloadPlugin_PLC_UnloadPlugin_{index}")) {
                        plugin.UnloadPlugin(plc.PluginInternalName, plc);
                    }
                } else {
                    if (ImGui.Button($"Load###loadPlugin_PLC_LoadPlugin_{index}")) {
                        plugin.LoadPlugin(plc.FilePath, plc);
                    }
                }

                
                ImGui.NextColumn();

                if (plc.Loaded) {
                    ImGui.Text("Unload to Remove");
                } else {
                    if (ImGui.Button($"Remove###deletePlugin_PLC_RemovePlugin_{index}")) {
                        doRemove = plc;
                    }
                }
                

                ImGui.NextColumn();
                index += 1;
                if (!changed) continue;
                plc.FilePath = path;
                plc.LoadAtStartup = loadAtStartup;
                plc.AutoReload = autoReload;
                Save();
            }


            ImGui.Columns(1);
            ImGui.SetNextItemWidth(-1);
            if (ImGui.Button("Add Plugin")) {
                PluginLoadConfigs.Add(new PluginLoadConfig() {FilePath = "", AutoReload = false, LoadAtStartup = false});
                Save();
            }


            if (doRemove != null) {
                PluginLoadConfigs.Remove(doRemove);
                Save();
            }
            
            ImGui.End();

            return drawConfig;
        }
    }
}


