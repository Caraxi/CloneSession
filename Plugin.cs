using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Dalamud;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using ImGuiNET;
using Newtonsoft.Json;

namespace CloneSession
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "Session Clone";

        private const string CommandName = "/clonesession";

        [PluginService] public static DalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] public static CommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static ChatGui ChatGui { get; private set; } = null!;

        private Configuration Config { get; }

        private bool showConfig;
        
        public static GameArguments GameArguments => GameArguments.Instance;

        public Plugin() {
            this.Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "A useful message to display in /xlhelp"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
            
            
#if DEBUG
            showConfig = true;
#endif

            if (Config.AutoClone) {
                if (MutexCount() < 2) {
                    CloneSession();
                }
            }
            
            
        }

        public void Dispose() {
            CommandManager.RemoveHandler(CommandName);
        }
        
        public static int MutexCount() {
            var c = 0;
            for (var i = 0; i < 10; i++) {
                var mutexName = $"Global\\6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game{i:00}";
                if (Mutex.TryOpenExisting(mutexName, out _)) {
                    c++;
                }
            }
            return c;
        }
        
        private void OnCommand(string command, string args) {
            if (args is "config" or "c" or "cfg") {
                ToggleConfigUI();
                return;
            }

            if (string.IsNullOrWhiteSpace(args)) {
                CloneSession();

            }
            
        }

        private void DrawUI() {
            if (showConfig) {
                if (ImGui.Begin($"{Name} Config", ref showConfig))
                {
                    var hasChanged = false;
                    hasChanged |= ImGui.InputTextWithHint("Custom User Path", GameArguments.UserPath, ref Config.UserPath, 1024);
                    hasChanged |= ImGui.InputTextWithHint("Custom Dalamud Path", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher"), ref Config.DalamudPath, 1024);
                    hasChanged |= ImGui.InputTextWithHint("Custom Game Path", GameArguments.GamePath, ref Config.GamePath, 1024);
                    
                    hasChanged |= ImGui.Checkbox("Automatically Clone on Startup", ref Config.AutoClone);
                    
                    if (ImGui.BeginCombo("Dalamud Stream", Config.SelectedDalamudStream)) {
                        foreach (var s in Config.DalamudStreams) {
                            if (ImGui.Selectable(s)) {
                                Config.SelectedDalamudStream = s;
                                hasChanged = true;
                            }
                        }
                        ImGui.EndCombo();
                    }
                    

                    if (hasChanged) PluginInterface.SavePluginConfig(Config);

                    var c = MutexCount();

                    if (c < 2) {
                        if (isStarting == false && ImGui.Button("Clone Session")) {
                            CloneSession();
                        }
                    } else {
                        ImGui.Text("Max clients reached.");
                    }
                }
                ImGui.End();
            }
        }

        private void ToggleConfigUI() {
            showConfig = !showConfig;
        }
        
        private bool isStarting;


        private string UpdateDalamud(string stream, string dalamudPath) {
            using var client = new WebClient();

            var json = client.DownloadString("https://kamori.goats.dev/Dalamud/Release/Meta");
            var versions = JsonConvert.DeserializeObject<Dictionary<string, DalamudVersionInfo>>(json);
            if (versions == null) return string.Empty;

            Config.DalamudStreams = versions.Keys.ToArray();
            PluginInterface.SavePluginConfig(Config);

            if (!versions.ContainsKey(stream)) return string.Empty;
            var version = versions[stream];

            var versionPath = Path.Combine(dalamudPath, "addon", "Hooks", version.AssemblyVersion);
            
            if (Directory.Exists(versionPath)) {
                return version.AssemblyVersion;
            }
            
            SimpleLog.Log($"Downloading Dalamud#{version.AssemblyVersion}");

            var zipFile = Path.Combine(PluginInterface.ConfigDirectory.FullName, $"{version.AssemblyVersion}.zip");
            if (File.Exists(zipFile)) File.Delete(zipFile);
            client.DownloadFile(version.DownloadUrl, zipFile);
            ZipFile.ExtractToDirectory(zipFile, versionPath);
            return version.AssemblyVersion;
        }
        
        private void CloneSession() {
            if (isStarting) return;

            isStarting = true;
            try {
                
                var dalamudAssembly = PluginInterface.GetType().Assembly;
                var service1T = dalamudAssembly.GetType("Dalamud.Service`1");
                if (service1T == null) return;
                var serviceInterfaceManager = service1T.MakeGenericType(typeof(DalamudStartInfo));
                var getter = serviceInterfaceManager.GetMethod("Get", BindingFlags.Static | BindingFlags.Public);
                if (getter == null) return;
                var existingStartInfo = (DalamudStartInfo?) getter.Invoke(null, null);
                if (existingStartInfo == null) return;
                
                var dalamudPath = string.IsNullOrWhiteSpace(Config.DalamudPath) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher") : Config.DalamudPath;
                
                
                
                Task.Run(async () => {
                    

                    SimpleLog.Log("Updating Assets");
                    var assetDir = await AssetManager.EnsureAssets(new DirectoryInfo(Path.Combine(dalamudPath, "dalamudAssets")));

                    SimpleLog.Log("Updating Dalamud");
                    var dalamudVersion = UpdateDalamud(Config.SelectedDalamudStream, dalamudPath);

                    if (string.IsNullOrEmpty(dalamudVersion)) {
                        SimpleLog.Log("Failed to update dalamud.");
                        return;
                    }
                    
                    
                    var parameters = string.Join(' ', new[] {
                        "launch", "-g", $"\"{(string.IsNullOrWhiteSpace(Config.GamePath) ? GameArguments.GamePath : Config.GamePath)}\"",
                        
                        // Dalamud Start Info
                        $"--dalamud-working-directory=\"{Path.Combine(dalamudPath,"addon","Hooks",dalamudVersion)}\"",
                        $"--dalamud-configuration-path=\"{Path.Combine(dalamudPath, "dalamudConfig.json")}\"",
                        $"--dalamud-plugin-directory=\"{Path.Combine(dalamudPath, "installedPlugins")}\"",
                        $"--dalamud-dev-plugin-directory=\"{Path.Combine(dalamudPath, "devPlugins")}\"",
                        $"--dalamud-asset-directory=\"{assetDir.FullName}\"",
                        $"--dalamud-delay-initialize=0",
                        $"--dalamud-client-language={GameArguments["Language"]}",
                        
                        "--",
                        // Game Arguments
                        $"DEV.DataPathType={GameArguments["DEV.DataPathType"]}",
                        $"DEV.MaxEntitledExpansionID={GameArguments["DEV.MaxEntitledExpansionID"]}",
                        $"DEV.TestSID={GameArguments["DEV.TestSID"]}",
                        $"DEV.UseSqPack={GameArguments["DEV.UseSqPack"]}",
                        $"SYS.Region={GameArguments["SYS.Region"]}",
                        $"language={GameArguments["language"]}",
                        $"resetConfig={GameArguments["resetConfig"]}",
                        $"ver={GameArguments["ver"]}",
                        $"UserPath=\"{(string.IsNullOrEmpty(Config.UserPath) ? GameArguments.UserPath : Config.UserPath)}\"",
                    });
                    
                    ImGui.SetClipboardText($"\"{Path.Combine(dalamudPath,"addon","Hooks",dalamudVersion, "Dalamud.Injector.exe")}\" {parameters}");
                    
                    
                    try {
                        SimpleLog.Log("Starting Dalamud in Cloned Process");
                        var dalamudInjector = new Process() {
                            StartInfo = {
                                FileName = Path.Combine(dalamudPath,"addon","Hooks","6c62bb1", "Dalamud.Injector.exe"),
                                WindowStyle = ProcessWindowStyle.Hidden,
                                CreateNoWindow = true,
                                Arguments = $"{parameters}",
                                WorkingDirectory = Path.Combine(dalamudPath,"addon","Hooks","dev"),
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                            }
                        };
                        dalamudInjector.OutputDataReceived += (sender, args) => { SimpleLog.Log(args.Data); };
                        dalamudInjector.Start();
                        dalamudInjector.WaitForExit();
                        SimpleLog.Log(dalamudInjector.StandardError.ReadToEnd());
                        SimpleLog.Log(dalamudInjector.StandardOutput.ReadToEnd()); ;
                        
                    } catch (Exception ex) {
                        SimpleLog.Error(ex);
                    }

                }).ContinueWith((_) => {
                    isStarting = false;
                });
                
            } catch (Exception ex) {
                SimpleLog.Error(ex);
            }
        }
        
    }
}
