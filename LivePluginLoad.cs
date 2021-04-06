using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Configuration;
using Dalamud.Plugin;
using ImGuiNET;

namespace LivePluginLoad {
    public class LivePluginLoad : IDalamudPlugin {
        public string Name => "LivePluginLoader";
        public DalamudPluginInterface PluginInterface { get; private set; }
        public LivePluginLoadConfig PluginConfig { get; private set; }

        private bool drawConfigWindow = false;
        private CancellationTokenSource taskController = new CancellationTokenSource();
        private CancellationToken cancellationToken;
        object pluginManager;
        private Dalamud.Dalamud dalamud;
        private PluginConfigurations pluginConfigs;
        private List<(IDalamudPlugin Plugin, PluginDefinition Definition, DalamudPluginInterface PluginInterface, bool IsRaw)> pluginsList;
        private ConstructorInfo pluginInterfaceConstructor;
        private List<(Assembly assembly, string path)> loadedAssemblies = new List<(Assembly assembly, string path)>();
        private readonly bool loadSubAssemblies = true;

        private Task reloadLoop;

        private readonly List<PluginLoadError> errorMessages = new List<PluginLoadError>();

        private bool isResolverTakenOver;
        private ResolveEventHandler originalAssemblyResolver;
        
        public void TakeoverAssemblyResolve() {
            if (isResolverTakenOver) return;
            PluginLog.Log("Attempting to takeover AppDomain.AssemblyResolve");
            isResolverTakenOver = true;
            try {
                var cd = AppDomain.CurrentDomain;

                PluginLog.Log($"Current Domain: {cd.Id}");
                var a = typeof(AppDomain);
                var b = a.GetField("_AssemblyResolve", BindingFlags.Instance | BindingFlags.NonPublic);
                var c = (ResolveEventHandler) b?.GetValue(cd);
                originalAssemblyResolver = c;
                
                b?.SetValue(cd, new ResolveEventHandler(((sender, args) => {

                    PluginLog.Log($"Resolving for: {args.RequestingAssembly.GetName().Name}");
                    PluginLog.Log($"Loaded Assemblies: {loadedAssemblies.Count}");

                    var f = loadedAssemblies.Where(la => la.assembly == args.RequestingAssembly).ToList();
                    
                    if (f.Count == 0) {
                        PluginLog.Log("Not a LivePlugin");
                        return c.Invoke(sender, args);
                    }

                    var l = f.Last();

                    PluginLog.Log($"Loading Sub Assembly for {l.path}");

                    var dirName = Path.GetDirectoryName(l.path);
                    if (dirName == null) {
                        return c.Invoke(sender, args);
                    }

                    var assemblyPath = Path.Combine(dirName, new AssemblyName(args.Name).Name + ".dll");

                    PluginLog.Log($"Loading Assembly: {assemblyPath}");

                    if (!File.Exists(assemblyPath)) {
                        PluginLog.LogError($"File Not Found: {assemblyPath}");
                        return c.Invoke(sender, args);
                    }

                    var assembly = Assembly.Load(File.ReadAllBytes(assemblyPath));

                    loadedAssemblies.Add((assembly, assemblyPath));

                    return assembly;
                })));


            } catch (Exception ex) {
                PluginLog.Log("Failed to takeover AppDomain.AssemblyResolve");
                PluginLog.LogError(ex.ToString());
            }
        }

        private void ReleaseAssemblyResolve() {
            if (!isResolverTakenOver) return;
            PluginLog.Log("Attempting to release AppDomain.AssemblyResolve");
            isResolverTakenOver = false;
            try {
                var cd = AppDomain.CurrentDomain;
                var a = typeof(AppDomain);
                var b = a.GetField("_AssemblyResolve", BindingFlags.Instance | BindingFlags.NonPublic);
                b?.SetValue(cd, originalAssemblyResolver);
            } catch (Exception ex) {
                PluginLog.Log("Failed to release AppDomain.AssemblyResolve");
                PluginLog.LogError(ex.ToString());
            }
        }

        public void Dispose() {
            taskController.Cancel();
            PluginInterface.UiBuilder.OnBuildUi -= this.BuildUI;
            PluginInterface.UiBuilder.OnOpenConfigUi -= OnConfigCommandHandler;
            PluginInterface.CommandManager.RemoveHandler("/plpl");
            PluginInterface.CommandManager.RemoveHandler("/plpl_load");
            while (reloadLoop != null && !reloadLoop.IsCompleted) Thread.Sleep(1);
            reloadLoop?.Dispose();
            PluginInterface?.Dispose();
        }

        public void Initialize(DalamudPluginInterface pluginInterface) {
            cancellationToken = taskController.Token;
            this.PluginInterface = pluginInterface;
            this.PluginConfig = (LivePluginLoadConfig) pluginInterface.GetPluginConfig() ?? new LivePluginLoadConfig();
            this.PluginConfig.Init(this, pluginInterface);

            dalamud = (Dalamud.Dalamud) pluginInterface.GetType()
                ?.GetField("dalamud", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(pluginInterface);
            
            pluginManager = dalamud?.GetType()
                ?.GetProperty("PluginManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(dalamud);

            pluginConfigs = (PluginConfigurations) pluginManager?.GetType()
                ?.GetField("pluginConfigs", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(pluginManager);

            pluginsList = (List<(IDalamudPlugin Plugin, PluginDefinition Definition, DalamudPluginInterface PluginInterface, bool IsRaw)>) pluginManager?.GetType()
                ?.GetProperty("Plugins", BindingFlags.Instance | BindingFlags.Public)
                ?.GetValue(pluginManager);

            pluginInterfaceConstructor = typeof(DalamudPluginInterface).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] {typeof(Dalamud.Dalamud), typeof(string), typeof(PluginConfigurations), typeof(PluginLoadReason)}, null);

            if (dalamud == null || pluginManager == null || pluginConfigs == null || pluginsList == null) {
                PluginLog.LogError("Failed to setup.");
                return;
            }

            PluginInterface.UiBuilder.OnBuildUi += this.BuildUI;
            pluginInterface.UiBuilder.OnOpenConfigUi += OnConfigCommandHandler; 

            if (PluginConfig.ForceDalamudDev)
            {
                var dalamudInterface = dalamud?.GetType()
                    ?.GetProperty("DalamudUi", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(dalamud);
                dalamudInterface?.GetType().GetField("isImguiDrawDevMenu", BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(dalamudInterface, true);
            }

            if (PluginConfig.DisablePanic) {
                var gameData = (Lumina.GameData) pluginInterface.Data?.GetType()?.GetField("gameData", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(pluginInterface.Data);
                if (gameData == null) {
                    PluginLog.Log("Failed to disable panic!");
                } else {
                    if (gameData.Options.PanicOnSheetChecksumMismatch) {
                        UpdatePanic();
                    }
                }
            }


            reloadLoop = Task.Run(() => {
                cancellationToken.WaitHandle.WaitOne(1000);
                if (cancellationToken.IsCancellationRequested) return;
                foreach (var plc in PluginConfig.PluginLoadConfigs.Where(plc => plc.LoadAtStartup == true)) {
                    if (cancellationToken.IsCancellationRequested) return;
                    LoadPlugin(plc.FilePath, plc);
                }

                while (!cancellationToken.IsCancellationRequested) {
                    cancellationToken.WaitHandle.WaitOne(1000);
                    if (cancellationToken.IsCancellationRequested) break;
                    foreach (var plc in PluginConfig.PluginLoadConfigs.Where(plc => plc.Loaded && plc.AutoReload)) {
                        if (cancellationToken.IsCancellationRequested) break;
                        var fi = new FileInfo(plc.FilePath);
                        if (plc.FileChanged != fi.LastWriteTime.Ticks) {
                            cancellationToken.WaitHandle.WaitOne(500);
                            if (cancellationToken.IsCancellationRequested) break;
                            var fi2 = new FileInfo(plc.FilePath);
                            if (fi2.LastWriteTime.Ticks == fi.LastWriteTime.Ticks) {
                                PluginLog.Log($"Changes Detected in {plc.PluginInternalName}");
                                plc.PerformReload = true;
                            }
                        }
                    }
                    if (cancellationToken.IsCancellationRequested) break;
                }
                PluginLog.Log("Closed change detection thread");
            }, cancellationToken);

            SetupCommands();
            if (PluginConfig.OpenAtStartup) {
                drawConfigWindow = true;
            }
        }

        public bool UnloadPlugin(string internalName, PluginLoadConfig pluginLoadConfig = null) {
           try {
               (IDalamudPlugin plugin, PluginDefinition definition, DalamudPluginInterface pluginInterface, bool IsRaw) = pluginsList.FirstOrDefault(p => p.Definition.InternalName == internalName);
               plugin?.Dispose();
               pluginsList.Remove((plugin, definition, pluginInterface, IsRaw));

               if (pluginLoadConfig != null) {
                   pluginLoadConfig.Loaded = false;
               }

               return true;
           } catch (Exception ex) {
               PluginLog.LogError("Failed to unload");
               PluginLog.LogError(ex.ToString());
               return false;
           }
        }

        
        public void LoadPlugin(string dllPath, PluginLoadConfig pluginLoadConfig = null) {
            if (!File.Exists(dllPath)) {
                PluginLog.LogError("File does not exist: {0}", dllPath);
                return;
            }

            FileInfo dllFile = new FileInfo(dllPath);

            var pdbPath = Path.Combine(Path.GetDirectoryName(dllPath), Path.GetFileNameWithoutExtension(dllPath) + ".pdb");

            if (pluginLoadConfig != null) {
                pluginLoadConfig.FileSize = dllFile.Length;
                pluginLoadConfig.FileChanged = dllFile.LastWriteTime.Ticks;
            }

            PluginLog.Log($"Attempting to load DLL at {dllFile.FullName}");

            Assembly pluginAssembly;

            var assemblyData = File.ReadAllBytes(dllFile.FullName);

            TakeoverAssemblyResolve();
            if (File.Exists(pdbPath)) {
                var pdbData = File.ReadAllBytes(pdbPath);
                pluginAssembly = Assembly.Load(assemblyData, pdbData);
            } else {
                pluginAssembly = Assembly.Load(assemblyData);
            }

            loadedAssemblies.Add((pluginAssembly, dllPath));

            var types = pluginAssembly.GetTypes();
            foreach (var type in types) {
                if (type.IsInterface || type.IsAbstract) {
                    continue;
                }

                if (type.GetInterface(typeof(IDalamudPlugin).FullName) != null) {
                    UnloadPlugin(type.Assembly.GetName().Name, pluginLoadConfig);

                    if (pluginsList.Any(x => x.Plugin.GetType().Assembly.GetName().Name == type.Assembly.GetName().Name)) {
                        PluginLog.LogError("Duplicate plugin found: {0}", dllFile.FullName);
                        ReleaseAssemblyResolve();
                        return;
                    }

                    var plugin = (IDalamudPlugin) Activator.CreateInstance(type);

                    var pluginDef = new PluginDefinition {
                        Author = "developer",
                        Name = plugin.Name,
                        InternalName = Path.GetFileNameWithoutExtension(dllFile.Name),
                        AssemblyVersion = plugin.GetType().Assembly.GetName().Version.ToString(),
                        Description = $"Loaded by {Name}",
                        ApplicableVersion = "any",
                        IsHide = false
                    };

                    var dalamudInterface = (DalamudPluginInterface) pluginInterfaceConstructor.Invoke(new object[] { dalamud, type.Assembly.GetName().Name, pluginConfigs, PluginLoadReason.Unknown });
                    try {
                        plugin.GetType()?.GetProperty("AssemblyLocation", BindingFlags.Public | BindingFlags.Instance)?.SetValue(plugin, dllFile.FullName);
                        plugin.GetType()?.GetMethod("SetLocation", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(plugin, new object[] { dllFile.FullName });
                    } catch {
                        // Ignored
                    }

                    try {
                        plugin.Initialize(dalamudInterface);
                        PluginLog.Log("Loaded Plugin: {0}", plugin.Name);

                        pluginsList.Add((plugin, pluginDef, dalamudInterface, true));
                        if (pluginLoadConfig != null) {
                            pluginLoadConfig.PluginInternalName = pluginDef.InternalName;
                            pluginLoadConfig.Loaded = true;
                        }
                    } catch (Exception ex) {
                        errorMessages.Add(new PluginLoadError(pluginLoadConfig, plugin, ex));
                        PluginLog.LogError("Failed to load plugin: {0}", plugin.Name);
                        PluginLog.LogError(ex.ToString());
                        PluginLog.Log("\n\n");
                    }
                }
            }
            
            ReleaseAssemblyResolve();
        }

        public class PluginLoadError {
            public PluginLoadConfig PluginLoadConfig { get; }
            public IDalamudPlugin Plugin { get; }
            public Exception Exception { get; }
            public bool Closed { get; set; }

            public PluginLoadError(PluginLoadConfig pluginLoadConfig, IDalamudPlugin plugin, Exception exception) {
                PluginLoadConfig = pluginLoadConfig;
                Plugin = plugin;
                Exception = exception;
            }
        }

        public List<(IDalamudPlugin Plugin, PluginDefinition Definition, DalamudPluginInterface PluginInterface, bool IsRaw)> PluginList => pluginsList;


        public void SetupCommands() {
            PluginInterface.CommandManager.AddHandler("/plpl", new Dalamud.Game.Command.CommandInfo(OnConfigCommandHandler) {
                HelpMessage = $"Open config window for {this.Name}",
                ShowInHelp = true
            });

            PluginInterface.CommandManager.AddHandler("/plpl_load", new Dalamud.Game.Command.CommandInfo(OnLoadCommandHandler) {
                HelpMessage = $"Load a plugin from a given path",
                ShowInHelp = true
            });
        }

        private void OnLoadCommandHandler(string command = "", string arguments = "") {
            PluginInterface.Framework.Gui.Chat.Print($"Loading Plugin: {arguments}");
            if (!string.IsNullOrEmpty(arguments)) {
                LoadPlugin(arguments);
            }
        }

        public void OnConfigCommandHandler(object a = null, object b = null) {
            drawConfigWindow = !drawConfigWindow;
        }

        private void BuildUI() {
            drawConfigWindow = drawConfigWindow && PluginConfig.DrawConfigUI();

            if (PluginConfig.TopBar) {
                ImGui.BeginMainMenuBar();
                if (ImGui.MenuItem("LivePluginLoader")) {
                    OnConfigCommandHandler();
                }
                
                ImGui.EndMainMenuBar();
            }
            
            try {
                foreach (var plc in PluginConfig.PluginLoadConfigs.Where(plc => plc.PerformReload)) {
                    plc.PerformReload = false;
                    plc.PerformLoad = true;
                    plc.PerformUnload = false;
                    UnloadPlugin(plc.PluginInternalName, plc);
                    return;
                }
            } catch (Exception ex) {
                PluginLog.LogError(ex, "Error in Reload Loop");
            }

            try {
                foreach (var plc in PluginConfig.PluginLoadConfigs.Where(plc => plc.PerformUnload)) {
                    plc.PerformLoad = false;
                    plc.PerformUnload = false;
                    plc.PerformReload = false;
                    UnloadPlugin(plc.PluginInternalName, plc);
                    return;
                }
            } catch (Exception ex) {
                PluginLog.LogError(ex, "Error in Unload Loop");
            }

            try {
                foreach (var plc in PluginConfig.PluginLoadConfigs.Where(plc => plc.PerformLoad)) {
                    plc.PerformLoad = false;
                    plc.PerformUnload = false;
                    plc.PerformReload = false;
                    LoadPlugin(plc.FilePath, plc);
                    return;
                }
            } catch (Exception ex) {
                PluginLog.LogError(ex, "Error in Load Loop");
            }
            
            foreach (var ex in errorMessages.Where(e => !e.Closed)) {
                var open = !ex.Closed;
                ImGui.Begin($"{ex.Plugin.Name} failed to load.", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse);
                ImGui.TextUnformatted(ex.Exception.ToString());
                ImGui.End();

                if (open) continue;
                foreach (var close in errorMessages.Where(e => ex.Plugin.Name == e.Plugin.Name)) {
                    close.Closed = true;
                }
            }
        }

        public void UpdatePanic() {
            var gameData = (Lumina.GameData) PluginInterface.Data.GetType()?.GetField("gameData", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(PluginInterface.Data);
            if (gameData != null) {
                gameData.Options.PanicOnSheetChecksumMismatch = !PluginConfig.DisablePanic;
                PluginLog.Log($"Set Panic: {!PluginConfig.DisablePanic}");
            } else {
                PluginLog.Log("Failed to get Lumina");
            }
        }
    }
}
