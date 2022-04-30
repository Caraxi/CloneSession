using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace CloneSession
{
    
    /*
     * Adapted from XIVLauncher
     * https://github.com/goatcorp/FFXIVQuickLauncher/blob/ffc369c86a17e3d9863b8ebdf3d741e5cc66dc3a/src/XIVLauncher.Common/Dalamud/AssetManager.cs
     */
    
    public static class AssetManager
    {
        private const string ASSET_STORE_URL = "https://kamori.goats.dev/Dalamud/Asset/Meta";

        internal class AssetInfo
        {
            public int Version { get; set; }
            public IReadOnlyList<Asset> Assets { get; set; }

            public class Asset
            {
                public string Url { get; set; }
                public string FileName { get; set; }
                public string Hash { get; set; }
            }
        }

        public static async Task<DirectoryInfo> EnsureAssets(DirectoryInfo baseDir)
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(4),
            };

            client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
            {
                NoCache = true,
            };

            using var sha1 = SHA1.Create();

            SimpleLog.Verbose("[DASSET] Starting asset download");

            var (isRefreshNeeded, info) = CheckAssetRefreshNeeded(baseDir);

            // NOTE(goat): We should use a junction instead of copying assets to a new folder. There is no C# API for junctions in .NET Framework.

            var assetsDir = new DirectoryInfo(Path.Combine(baseDir.FullName, info.Version.ToString()));
            var devDir = new DirectoryInfo(Path.Combine(baseDir.FullName, "dev"));

            foreach (var entry in info.Assets)
            {
                var filePath = Path.Combine(assetsDir.FullName, entry.FileName);
                var filePathDev = Path.Combine(devDir.FullName, entry.FileName);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePathDev)!);
                }
                catch
                {
                    // ignored
                }

                var refreshFile = false;

                if (File.Exists(filePath) && !string.IsNullOrEmpty(entry.Hash))
                {
                    try
                    {
                        using var file = File.OpenRead(filePath);
                        var fileHash = sha1.ComputeHash(file);
                        var stringHash = BitConverter.ToString(fileHash).Replace("-", "");
                        refreshFile = stringHash != entry.Hash;
                        SimpleLog.Verbose($"[DASSET] {entry.FileName} has {stringHash}, remote {entry.Hash}");
                    }
                    catch (Exception ex)
                    {
                        SimpleLog.Error(ex);
                    }
                }

                if (!File.Exists(filePath) || isRefreshNeeded || refreshFile)
                {
                    SimpleLog.Verbose("[DASSET] Downloading {0} to {1}...", entry.Url, entry.FileName);

                    var request = await client.GetAsync(entry.Url + "?t=" + DateTime.Now.Ticks).ConfigureAwait(true);
                    request.EnsureSuccessStatusCode();
                    File.WriteAllBytes(filePath, await request.Content.ReadAsByteArrayAsync().ConfigureAwait(true));

                    try
                    {
                        File.Copy(filePath, filePathDev, true);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            if (isRefreshNeeded)
                SetLocalAssetVer(baseDir, info.Version);

            SimpleLog.Verbose("[DASSET] Assets OK at {0}", assetsDir.FullName);
            
            return assetsDir;
        }

        private static string GetAssetVerPath(DirectoryInfo baseDir)
        {
            return Path.Combine(baseDir.FullName, "asset.ver");
        }

        /// <summary>
        ///     Check if an asset update is needed. When this fails, just return false - the route to github
        ///     might be bad, don't wanna just bail out in that case
        /// </summary>
        /// <param name="baseDir">Base directory for assets</param>
        /// <returns>Update state</returns>
        private static (bool isRefreshNeeded, AssetInfo info) CheckAssetRefreshNeeded(DirectoryInfo baseDir)
        {
            using var client = new WebClient();

            var localVerFile = GetAssetVerPath(baseDir);
            var localVer = 0;

            try
            {
                if (File.Exists(localVerFile))
                    localVer = int.Parse(File.ReadAllText(localVerFile));
            }
            catch (Exception ex)
            {
                // This means it'll stay on 0, which will redownload all assets - good by me
                SimpleLog.Error(ex, "[DASSET] Could not read asset.ver");
            }

            var remoteVer = JsonConvert.DeserializeObject<AssetInfo>(client.DownloadString(ASSET_STORE_URL));

            SimpleLog.Verbose($"[DASSET] Ver check - local:{localVer} remote:{remoteVer?.Version}");

            var needsUpdate = remoteVer?.Version > localVer;

            return (needsUpdate, remoteVer);
        }

        private static void SetLocalAssetVer(DirectoryInfo baseDir, int version)
        {
            try
            {
                var localVerFile = GetAssetVerPath(baseDir);
                File.WriteAllText(localVerFile, version.ToString());
            }
            catch (Exception e)
            {
                SimpleLog.Error(e, "[DASSET] Could not write local asset version");
            }
        }
    }
}