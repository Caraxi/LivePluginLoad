using Dalamud.Configuration;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;


namespace LivePluginLoad {
    public class LivePluginLoadConfig : IPluginConfiguration, IDisposable {
        [NonSerialized] private DalamudPluginInterface pluginInterface;

        [NonSerialized] private LivePluginLoad plugin;

        public int Version { get; set; }

        public List<PluginLoadConfig> PluginLoadConfigs { get; set; } = new List<PluginLoadConfig>();

        [NonSerialized]
        private bool browsing;

        public bool OpenAtStartup { get; set; } = true;
        public bool TopBar { get; set; } = true;

        public bool DisablePanic { get; set; } = false;

        public bool ForceDalamudDev { get; set; } = true;
        public bool ForceDalamudLog { get; set; } = false;

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

            var openAtStartup = OpenAtStartup;
            if (ImGui.Checkbox("Open at startup", ref openAtStartup)) {
                OpenAtStartup = openAtStartup;
                Save();
            }
            
            ImGui.SameLine();
            var topBar = TopBar;
            if (ImGui.Checkbox("Show Window Header Button", ref topBar)) {
                TopBar = topBar;
                Save();
            }

            ImGui.SameLine();
            var forceDev = ForceDalamudDev;
            if (ImGui.Checkbox("Enable Dalamud Dev Menu", ref forceDev)) {
                ForceDalamudDev = forceDev;
                Save();
            }
            
            ImGui.SameLine();
            var forceLog = ForceDalamudLog;
            if (ImGui.Checkbox("Enable Dalamud Log Window", ref forceLog)) {
                ForceDalamudLog = forceLog;
                Save();
            }
            
            ImGui.SameLine();
            var disablePanic = DisablePanic;
            if (ImGui.Checkbox("Disable Lumina Panic", ref disablePanic)) {
                DisablePanic = disablePanic;
                plugin.UpdatePanic();
                Save();
            }

            
            ImGui.Columns(5);
            ImGui.SetColumnWidth(0, ImGui.GetWindowWidth() - 350);
            ImGui.SetColumnWidth(1, 60);
            ImGui.SetColumnWidth(2, 60);
            ImGui.SetColumnWidth(3, 90);
            ImGui.SetColumnWidth(4, 150);
            ImGui.Separator();
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
            ImGui.NewLine();
            ImGui.Text("Remove/Config");
            ImGui.NextColumn();
            ImGui.Separator();
            
            int index = 0;

            var spacing = ImGui.GetStyle().ItemSpacing;

            var bSizeLoadUnload = new Vector2(90 - (spacing.X * 2), 24);
            var bSizeRemove = new Vector2(150 - (spacing.X * 4), 24);

            PluginLoadConfig doRemove = null;
            
            foreach (var plc in PluginLoadConfigs) {
                var path = plc.FilePath;
                var loadAtStartup = plc.LoadAtStartup;
                var autoReload = plc.AutoReload;
                var changed = false;

                ImGui.SetNextItemWidth(-1);
                changed = ImGui.InputText($"###inputText_PLC_Path_{index}", ref path, 512);

                if (ImGui.BeginPopupContextItem($"###inputTextContext_PLC_Path_{index}")) {
                    if (ImGui.Selectable("Browse")) {
                        if (!browsing) {
                            browsing = true;
                            var b = new Browse((result, filePath) => {
                                if (result == DialogResult.OK) {
                                    plc.FilePath = filePath;
                                    Save();
                                }

                                browsing = false;
                            });
                            var t = new Thread(b.BrowseDLL);
                            t.SetApartmentState(ApartmentState.STA);
                            t.Start();
                        }
                    }

                    ImGui.EndPopup();
                }

                ImGui.NextColumn();
                changed = ImGui.Checkbox($"###checkbox_PLC_LoadAtStartup_{index}", ref loadAtStartup) || changed;
                ImGui.NextColumn();
                ImGui.SameLine(spacing.X);
                changed = ImGui.Checkbox($"###checkbox_PLC_AutoReload_{index}", ref autoReload) || changed;
                ImGui.NextColumn();

                if (plc.Loaded) {
                    if (ImGui.Button($"Unload###unloadPlugin_PLC_UnloadPlugin_{index}", bSizeLoadUnload)) {
                        plc.PerformUnload = true;
                        plc.PerformReload = false;
                        plc.PerformLoad = false;
                    }
                } else {
                    if (ImGui.Button($"Load###loadPlugin_PLC_LoadPlugin_{index}", bSizeLoadUnload)) {
                        plc.PerformLoad = true;
                        plc.PerformUnload = false;
                        plc.PerformReload = false;
                    }
                }
                
                ImGui.NextColumn();
                
                if (plc.Loaded) {

                    var loadedPlugin = plugin.PluginList.Where(p => p.Definition.InternalName == plc.PluginInternalName).Select(p => p.PluginInterface).FirstOrDefault();

                    var getHasConfigUi = loadedPlugin.UiBuilder.GetType().GetProperty("HasConfigUi", BindingFlags.Instance | BindingFlags.NonPublic);
                    var openConfigUi   = loadedPlugin.UiBuilder.GetType().GetMethod("OpenConfigUi", BindingFlags.Instance  | BindingFlags.NonPublic);

                    if ((bool) getHasConfigUi.GetValue(loadedPlugin.UiBuilder)) {
                        if (ImGui.Button($"Open Config###pluginConfig_PLC_OpenConfig_{index}", bSizeRemove))
                        {
                            openConfigUi.Invoke(loadedPlugin.UiBuilder, null);
                        }
                    } else {
                        ImGui.Text("Unload to Remove");
                    }

                } else {
                    if (ImGui.Button($"Remove###deletePlugin_PLC_RemovePlugin_{index}", bSizeRemove)) {
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

        public void Dispose() {
            
        }
    }
}


