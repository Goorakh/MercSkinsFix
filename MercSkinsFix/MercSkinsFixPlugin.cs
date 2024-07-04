using BepInEx;
using BepInEx.Configuration;
using RiskOfOptions;
using RiskOfOptions.Options;
using RoR2;
using System;
using System.Diagnostics;
using System.IO;
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
        public const string PluginVersion = "1.0.0";

        internal static MercSkinsFixPlugin Instance { get; private set; }

        void Awake()
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            Log.Init(Logger);

            Instance = SingletonHelper.Assign(Instance, this);

            stopwatch.Stop();
            Log.Info_NoCallerPrefix($"Initialized in {stopwatch.Elapsed.TotalSeconds:F2} seconds");

            DelegateUtils.Append(ref RoR2Application.onLoad, onLoad);
        }

        void OnDestroy()
        {
            Instance = SingletonHelper.Unassign(Instance, this);
        }

        void onLoad()
        {
            SkinDef[] vanillaMercSkinDefs = [
                Addressables.LoadAssetAsync<SkinDef>("RoR2/Base/Merc/skinMercDefault.asset").WaitForCompletion(),
                Addressables.LoadAssetAsync<SkinDef>("RoR2/Base/Merc/skinMercAlt.asset").WaitForCompletion(),
                Addressables.LoadAssetAsync<SkinDef>("RoR2/Base/Merc/skinMercAltPrisoner.asset").WaitForCompletion(),
            ];

            GameObject mercBodyPrefab = BodyCatalog.FindBodyPrefab("MercBody");

            ModelLocator modelLocator = mercBodyPrefab.GetComponent<ModelLocator>();

            GameObject modelRoot = modelLocator.modelTransform.gameObject;

            Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            
            Renderer[] preDevotionRenderersList = [
                modelRoot.transform.Find("MercArmature/ROOT/base/stomach/chest/PreDashEffect/ContractingEnergy")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercArmature/ROOT/base/stomach/chest/PreDashEffect/FireEmission")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercArmature/ROOT/base/stomach/chest/PreDashEffect/BrightRing")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercMesh")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercSwordMesh")?.GetComponent<Renderer>(),
            ];

            SkinDef[] skins = BodyCatalog.GetBodySkins(BodyCatalog.FindBodyIndex(mercBodyPrefab));

            const string SETTING_MOD_GUID = PluginGUID;
            const string SETTING_MOD_NAME = "Mercenary Skins Fix";

            foreach (SkinDef skin in skins)
            {
                if (Array.IndexOf(vanillaMercSkinDefs, skin) >= 0)
                    continue;

                void setApplyToSkin(bool apply)
                {
                    Renderer[] currentRenderers = apply ? renderers : preDevotionRenderersList;
                    Renderer[] targetRenderers = apply ? preDevotionRenderersList : renderers;

                    for (int i = 0; i < skin.rendererInfos.Length; i++)
                    {
                        ref CharacterModel.RendererInfo rendererInfo = ref skin.rendererInfos[i];
                        if (!rendererInfo.renderer)
                            continue;

                        int rendererIndex = Array.IndexOf(currentRenderers, rendererInfo.renderer);
                        if (rendererIndex >= 0 && rendererIndex < targetRenderers.Length)
                        {
                            Log.Info($"Changing {skin.name} RendererInfo renderer {rendererInfo.renderer} -> {targetRenderers[rendererIndex]}");
                            rendererInfo.renderer = targetRenderers[rendererIndex];
                        }
                    }

                    for (int i = 0; i < skin.meshReplacements.Length; i++)
                    {
                        ref SkinDef.MeshReplacement meshReplacement = ref skin.meshReplacements[i];
                        if (!meshReplacement.renderer)
                            continue;

                        int rendererIndex = Array.IndexOf(currentRenderers, meshReplacement.renderer);
                        if (rendererIndex >= 0 && rendererIndex < targetRenderers.Length)
                        {
                            Log.Info($"Changing {skin.name} MeshReplacement renderer {meshReplacement.renderer} -> {targetRenderers[rendererIndex]}");
                            meshReplacement.renderer = targetRenderers[rendererIndex];
                        }
                    }

                    if (skin.runtimeSkin != null)
                    {
                        skin.runtimeSkin = null;
                        skin.Bake();
                    }
                }

                ConfigEntry<bool> shouldApplyToSkinConfig = Config.Bind("Skins", $"Fix {Language.GetString(skin.nameToken, "en")} ({skin.name})", false, "Apply the fix to this skin");

                ModSettingsManager.AddOption(new CheckBoxOption(shouldApplyToSkinConfig), SETTING_MOD_GUID, SETTING_MOD_NAME);

                if (shouldApplyToSkinConfig.Value)
                {
                    setApplyToSkin(true);
                }

                shouldApplyToSkinConfig.SettingChanged += (s, e) =>
                {
                    SettingChangedEventArgs settingChangedEvent = e as SettingChangedEventArgs;

                    bool applyToSkin = (bool)settingChangedEvent.ChangedSetting.BoxedValue;

                    setApplyToSkin(applyToSkin);
                };
            }

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
    }
}
