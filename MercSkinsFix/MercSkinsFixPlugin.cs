using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using MonoMod.RuntimeDetour;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;
using RoR2BepInExPack.GameAssetPaths;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace MercSkinsFix
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class MercSkinsFixPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Gorakh";
        public const string PluginName = "MercSkinsFix";
        public const string PluginVersion = "1.2.1";

        internal static MercSkinsFixPlugin Instance { get; private set; }

        static readonly Dictionary<Assembly, BepInEx.PluginInfo> _assemblyPluginLookup = [];
        static readonly Dictionary<SkinDef, Assembly> _skinOwnerAssemblies = [];

        static readonly Dictionary<Assembly, DateTime?> _assemblyTimestampLookup = [];

        Hook _scriptableObjectCreateInstanceHook;

        void Awake()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            Log.Init(Logger);

            Instance = SingletonHelper.Assign(Instance, this);

            _scriptableObjectCreateInstanceHook = new Hook(SymbolExtensions.GetMethodInfo(() => ScriptableObject.CreateInstance(default(Type))), ScriptableObject_CreateInstance);

            stopwatch.Stop();
            Log.Info_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalSeconds:F2} seconds");

            DelegateUtils.Append(ref RoR2Application.onLoad, OnLoad);
        }

        void OnDestroy()
        {
            Instance = SingletonHelper.Unassign(Instance, this);

            _scriptableObjectCreateInstanceHook?.Dispose();
            _scriptableObjectCreateInstanceHook = null;
        }

        delegate ScriptableObject orig_ScriptableObject_CreateInstance(Type type);
        private static ScriptableObject ScriptableObject_CreateInstance(orig_ScriptableObject_CreateInstance orig, Type type)
        {
            ScriptableObject obj = orig(type);

            try
            {
            if (obj is SkinDef skinDef)
            {
                StackTrace stackTrace = new StackTrace();

                for (int i = 0; i < stackTrace.FrameCount; i++)
                {
                    StackFrame frame = stackTrace.GetFrame(i);

                    Assembly assembly = frame?.GetMethod()?.DeclaringType?.Assembly;
                    if (assembly == null || assembly == Assembly.GetExecutingAssembly())
                        continue;

                    if (!_assemblyPluginLookup.TryGetValue(assembly, out BepInEx.PluginInfo plugin))
                    {
                        plugin = null;

                        foreach (BepInEx.PluginInfo pluginInfo in Chainloader.PluginInfos.Values)
                        {
                            if (pluginInfo.Instance && pluginInfo.Instance.GetType().Assembly == assembly)
                            {
                                plugin = pluginInfo;
                                break;
                            }
                        }

                        _assemblyPluginLookup.Add(assembly, plugin);
                    }

                    if (plugin == null)
                        continue;

                    if (!_assemblyTimestampLookup.ContainsKey(assembly))
                    {
                        DateTime? assemblyTimestamp = null;

                        string assemblyLocation = assembly.Location;
                        if (!string.IsNullOrEmpty(assemblyLocation) && File.Exists(assemblyLocation))
                        {
                            // https://stackoverflow.com/questions/2050396/getting-the-date-of-a-net-assembly

                            const int peHeaderOffset = 60;
                            const int linkerTimestampOffset = 8;

                            try
                            {
                                using FileStream fs = File.Open(assemblyLocation, FileMode.Open, FileAccess.Read, FileShare.Read);

                                byte[] buffer = new byte[2048];
                                int bytesRead = fs.Read(buffer, 0, 2048);
                                if (bytesRead >= peHeaderOffset)
                                {
                                    int linkerTimestampLocation = BitConverter.ToInt32(buffer, peHeaderOffset) + linkerTimestampOffset;
                                    if (bytesRead >= linkerTimestampLocation + sizeof(int))
                                    {
                                        int timestampSeconds = BitConverter.ToInt32(buffer, linkerTimestampLocation);

                                        DateTime linkerTimestampUTC = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestampSeconds);

                                        // Timestamp is not *always* number of seconds
                                        // TODO: Handle other cases? Filter out for now
                                        if (linkerTimestampUTC > new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) && linkerTimestampUTC < DateTime.UtcNow)
                                        {
                                            assemblyTimestamp = linkerTimestampUTC;
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error_NoCallerPrefix($"Failed to determine assembly date for {assembly.GetName()}: {e}");
                            }
                        }

                        _assemblyTimestampLookup.Add(assembly, assemblyTimestamp);
                    }

                    _skinOwnerAssemblies.Add(skinDef, assembly);
                    break;
                }
            }
            }
            catch (Exception e)
            {
                Log.Error_NoCallerPrefix(e);
            }

            return obj;
        }

        private void OnLoad()
        {
            SkinDef[] vanillaMercSkinDefs =
            [
                Addressables.LoadAssetAsync<SkinDef>(RoR2_Base_Merc.skinMercDefault_asset).WaitForCompletion(),
                Addressables.LoadAssetAsync<SkinDef>(RoR2_Base_Merc.skinMercAlt_asset).WaitForCompletion(),
                Addressables.LoadAssetAsync<SkinDef>(RoR2_Base_Merc.skinMercAltPrisoner_asset).WaitForCompletion(),
                Addressables.LoadAssetAsync<SkinDef>(RoR2_Base_Merc.skinMercAltColossus_asset).WaitForCompletion(),
            ];

            const string SETTING_MOD_GUID = PluginGUID;
            const string SETTING_MOD_NAME = "Mercenary Skins Fix";

            Dictionary<string, HashSet<string>> usedSectionKeys = [];

            Config.SaveOnConfigSet = false;

            bool anySettingAdded = false;

            foreach (SkinDef skin in SkinCatalog.GetBodySkinDefs(BodyCatalog.FindBodyIndex("MercBody")))
            {
                if (Array.IndexOf(vanillaMercSkinDefs, skin) != -1)
                    continue;

                Assembly ownerAssembly = _skinOwnerAssemblies.GetValueSafe(skin);
                BepInEx.PluginInfo ownerPlugin = ownerAssembly != null ? _assemblyPluginLookup.GetValueSafe(ownerAssembly) : null;

                string sectionName = "Unknown";
                bool fixEnabledByDefault = false;
                if (ownerPlugin != null && ownerAssembly != null)
                {
                    if (_assemblyTimestampLookup.TryGetValue(ownerAssembly, out DateTime? assemblyTimestamp) && assemblyTimestamp.HasValue)
                    {
                        DateTime devotionReleaseDate = new DateTime(2024, 5, 20, 0, 0, 0, DateTimeKind.Utc);
                        if (assemblyTimestamp.Value < devotionReleaseDate)
                        {
                            fixEnabledByDefault = true;
                        }
                    }

                    sectionName = $"{ownerPlugin.Metadata.Name} ({System.IO.Path.GetFileName(ownerAssembly.Location)})";
                }

                string skinName;
                if (string.IsNullOrEmpty(skin.nameToken))
                {
                    if (string.IsNullOrEmpty(skin.name))
                    {
                        skinName = skin.skinIndex.ToString();
                    }
                    else
                    {
                        skinName = skin.name;
                    }
                }
                else
                {
                    if (string.IsNullOrEmpty(skin.name))
                    {
                        skinName = Language.GetString(skin.nameToken, "en");
                    }
                    else
                    {
                        skinName = $"{Language.GetString(skin.nameToken, "en")} ({skin.name})";
                    }
                }

                // Remove rich text tags
                string richTextRemovedSkinName = Regex.Replace(skinName, @"<[^<>]+>", string.Empty);
                if (!string.IsNullOrEmpty(richTextRemovedSkinName))
                {
                    skinName = richTextRemovedSkinName;
                }

                skinName = skinName.FilterConfigKey();
                if (string.IsNullOrEmpty(skinName))
                {
                    System.Random random = new System.Random(skin.nameToken.GetHashCode());

                    StringBuilder sb = HG.StringBuilderPool.RentStringBuilder();

                    byte[] buffer = new byte[16];
                    random.NextBytes(buffer);

                    for (int i = 0; i < buffer.Length; i++)
                    {
                        sb.Append(buffer[i].ToString("X2"));
                    }

                    skinName = $"INVALID SKIN NAME {sb}";

                    HG.StringBuilderPool.ReturnStringBuilder(sb);
                }

                if (usedSectionKeys.TryGetValue(sectionName, out HashSet<string> usedKeys))
                {
                    if (!usedKeys.Add(skinName))
                    {
                        string unmodifiedSkinName = skinName;

                        int i = 1;
                        do
                        {
                            skinName = $"{unmodifiedSkinName} ({i})";
                            i++;
                        } while (!usedKeys.Add(skinName));
                    }
                }
                else
                {
                    usedSectionKeys.Add(sectionName, [skinName]);
                }

                ConfigEntry<bool> shouldApplyToSkinConfig = Config.Bind(sectionName.FilterConfigKey(), $"Fix {skinName}", fixEnabledByDefault, "Apply the fix to this skin");

                ModSettingsManager.AddOption(new CheckBoxOption(shouldApplyToSkinConfig), SETTING_MOD_GUID, SETTING_MOD_NAME);

                anySettingAdded = true;

                if (shouldApplyToSkinConfig.Value)
                {
                    RebakeSkin.SetApplyToSkin(skin, true);
                }

                shouldApplyToSkinConfig.SettingChanged += (s, e) =>
                {
                    bool applyToSkin = (bool)(e as SettingChangedEventArgs).ChangedSetting.BoxedValue;

                    RebakeSkin.SetApplyToSkin(skin, applyToSkin);
                };
            }

            Config.SaveOnConfigSet = true;
            Config.Save();

            if (anySettingAdded)
            {
                ModSettingsManager.SetModDescription("Fix broken Mercenary skins after the Devotion update", SETTING_MOD_GUID, SETTING_MOD_NAME);

                FileInfo iconFile = null;

                DirectoryInfo dir = new DirectoryInfo(System.IO.Path.GetDirectoryName(Info.Location));
                do
                {
                    FileInfo[] files = dir.GetFiles("icon.png", SearchOption.TopDirectoryOnly);
                    if (files != null && files.Length > 0)
                    {
                        iconFile = files[0];
                        break;
                    }

                    dir = dir.Parent;
                } while (dir != null && !string.Equals(dir.Name, "plugins", StringComparison.OrdinalIgnoreCase));

                if (iconFile != null)
                {
                    Texture2D iconTexture = new Texture2D(256, 256);
                    if (iconTexture.LoadImage(File.ReadAllBytes(iconFile.FullName)))
                    {
                        Sprite iconSprite = Sprite.Create(iconTexture, new Rect(0f, 0f, iconTexture.width, iconTexture.height), new Vector2(0.5f, 0.5f));

                        ModSettingsManager.SetModIcon(iconSprite, SETTING_MOD_GUID, SETTING_MOD_NAME);
                    }
                }
            }

            _scriptableObjectCreateInstanceHook?.Dispose();
            _scriptableObjectCreateInstanceHook = null;
        }
    }
}
