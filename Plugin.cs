using System;
using System.Collections.Generic;
using System.Diagnostics;
using Dalamud.Game.Command;
using Dalamud.Plugin;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Numerics;
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
            this.Config = Configuration.Load();
            
            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "A useful message to display in /xlhelp"
            });

            PluginInterface.UiBuilder.Draw += DrawUI;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
            
            
#if DEBUG
            showConfig = true;
#endif

            if (Config.FreeMutex) {
                ClearMutex();
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

        public void ClearMutex() {
            if (Config.FreeMutex && MutexCount() > 0) {
                foreach (var hi in HandleUtil.GetHandles().Where(t => t.Type == HandleUtil.HandleType.Mutant)) {
                    if (!string.IsNullOrEmpty(hi.Name) && 
                        (hi.Name.EndsWith("6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game00") || 
                         hi.Name.EndsWith("6AA83AB5-BAC4-4a36-9F66-A309770760CB_ffxiv_game01"))
                       ) {
                        SimpleLog.Log($"Close Mutex: {hi.Name}");
                        hi.Close();
                    }
                }
            }
        }
        
        private void OnCommand(string command, string args) {
            if (args is "config" or "c" or "cfg") {
                ToggleConfigUI();
                return;
            }
            
            if (string.IsNullOrWhiteSpace(args)) {
                CloneSession(Config.DefaultProfile);
            }

            foreach (var p in Config.Profiles) {
                if (p.ProfileName.Equals(args, StringComparison.InvariantCultureIgnoreCase)) {
                    CloneSession(p);
                    return;
                }
            }
            
        }

        private ProfileConfig? deletedItem;
        
        private void DrawUI() {
            if (showConfig) {
                if (ImGui.Begin($"{Name} Config", ref showConfig))
                {
                    var hasChanged = false;
                    
                    hasChanged |= ImGui.Checkbox("Remove game instance limit", ref Config.FreeMutex);
                    
                    ImGui.Separator();
                    
                    if (ImGui.BeginTabBar("profileTabs")) {

                        var anyTabOpen = false;
                        
                        int i;
                        for (i = 0; i < Config.Profiles.Count; i++) {
                            var p = Config.Profiles[i];
                            var n = string.IsNullOrEmpty(p.ProfileName) ? $"Profile {i}" : p.ProfileName;
                            var isDefault = Config.DefaultProfileIndex == i;
                            ImGui.PushStyleColor(ImGuiCol.Text, isDefault ? 0xFF00FFFF : 0xFFFFFFFF);
                            
                            var tabOpen = ImGui.BeginTabItem($"{n}###profile#{i}");
                            anyTabOpen |= tabOpen;
                            ImGui.PopStyleColor();
                            if (tabOpen) {
                                
                                if (ImGui.Checkbox("Default Profile", ref isDefault)) {
                                    hasChanged = true;
                                    if (isDefault) Config.DefaultProfileIndex = i;
                                }
                                ImGui.SameLine();
                                ImGui.Dummy(new Vector2(50, 1));
                                ImGui.SameLine();
                                if (ImGui.Button("Copy Profile")) {
                                    Config.Profiles.Add(new ProfileConfig() {
                                        DalamudPath = p.DalamudPath,
                                        ProfileName = p.ProfileName,
                                        GamePath = p.GamePath,
                                        SelectedDalamudStream = p.SelectedDalamudStream,
                                        UserPath = p.UserPath
                                    });
                                }
                                if (!isDefault) {
                                    ImGui.SameLine();
                                    if (ImGui.Button("Delete Profile")) {
                                        hasChanged = true;
                                        deletedItem = p;
                                        Config.Profiles.RemoveAt(i);
                                        if (Config.DefaultProfileIndex > i) Config.DefaultProfileIndex--;
                                        break;
                                    }
                                }
                                
                                
                                hasChanged |= ImGui.InputTextWithHint("Profile Name", n, ref p.ProfileName, 1024);
                                hasChanged |= ImGui.InputTextWithHint("Custom User Path", GameArguments.UserPath, ref p.UserPath, 1024);
                                hasChanged |= ImGui.InputTextWithHint("Custom Dalamud Path", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher"), ref p.DalamudPath, 1024);
                                hasChanged |= ImGui.InputTextWithHint("Custom Game Path", GameArguments.GamePath, ref p.GamePath, 1024);

                                if (ImGui.BeginCombo("Dalamud Stream", p.SelectedDalamudStream)) {
                                    foreach (var s in Config.DalamudStreams) {
                                        if (ImGui.Selectable(s)) {
                                            p.SelectedDalamudStream = s;
                                            hasChanged = true;
                                        }
                                    }
                                    ImGui.EndCombo();
                                }
                                
                                
                                var c = MutexCount();

                                if (c < 2 || Config.FreeMutex) {
                                    if (isStarting == false && ImGui.Button("Clone Session")) {
                                        CloneSession(p);
                                    }
                                } else {
                                    ImGui.Text("Max clients reached.");
                                }
                                
                                ImGui.EndTabItem();
                            }
                        }


                        if (ImGui.TabItemButton($"+###profile#{i}")) {
                            Config.Profiles.Add(new ProfileConfig());
                            hasChanged = true;
                        }
                        
                        if (!anyTabOpen && deletedItem != null) {
                            ImGui.Text($"{deletedItem.ProfileName} Deleted.");
                            if (ImGui.Button("Undo")) {
                                Config.Profiles.Add(deletedItem);
                            }
                        }
                        

                        ImGui.EndTabBar();
                    }


                    if (hasChanged) Config.Save();

                    if (hasChanged) {
                        ClearMutex();
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
            Config.Save();

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
        
        private void CloneSession(ProfileConfig? config = null) {
            config ??= Config.DefaultProfile;
            if (isStarting) return;

            ClearMutex();

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
                
                var dalamudPath = string.IsNullOrWhiteSpace(config.DalamudPath) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher") : config.DalamudPath;
                
                
                
                Task.Run(async () => {
                    

                    SimpleLog.Log("Updating Assets");
                    var assetDir = await AssetManager.EnsureAssets(new DirectoryInfo(Path.Combine(dalamudPath, "dalamudAssets")));

                    SimpleLog.Log("Updating Dalamud");
                    var dalamudVersion = UpdateDalamud(config.SelectedDalamudStream, dalamudPath);

                    if (string.IsNullOrEmpty(dalamudVersion)) {
                        SimpleLog.Log("Failed to update dalamud.");
                        return;
                    }
                    
                    
                    var parameters = string.Join(' ', new[] {
                        "launch", "-g", $"\"{(string.IsNullOrWhiteSpace(config.GamePath) ? GameArguments.GamePath : config.GamePath)}\"",
                        
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
                        $"UserPath=\"{(string.IsNullOrEmpty(config.UserPath) ? GameArguments.UserPath : config.UserPath)}\"",
                    });
                    
                    ImGui.SetClipboardText($"\"{Path.Combine(dalamudPath,"addon","Hooks",dalamudVersion, "Dalamud.Injector.exe")}\" {parameters}");
                    
                    
                    try {
                        SimpleLog.Log("Starting Dalamud in Cloned Process");
                        var dalamudInjector = new Process() {
                            StartInfo = {
                                FileName = Path.Combine(dalamudPath,"addon","Hooks",dalamudVersion, "Dalamud.Injector.exe"),
                                WindowStyle = ProcessWindowStyle.Normal,
                                CreateNoWindow = false,
                                Arguments = $"{parameters}",
                                WorkingDirectory = Path.Combine(dalamudPath,"addon","Hooks",dalamudVersion),
                            }
                        };

                        dalamudInjector.Start();
                        await dalamudInjector.WaitForExitAsync();
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
