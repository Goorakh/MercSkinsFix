using System;
using HG;
using RoR2;
using UnityEngine;

namespace MercSkinsFix
{
    public static class RebakeSkin
    {
#pragma warning disable CS0618 // Type or member is obsolete
        public static void SetApplyToSkin(SkinDef skin, bool apply)
        {
            var modelRoot = BodyCatalog.FindBodyPrefab("MercBody").GetComponent<ModelLocator>().modelTransform.gameObject;

            Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);

            Renderer[] preDevotionRenderersList = [
                modelRoot.transform.Find("MercArmature/ROOT/base/stomach/chest/PreDashEffect/ContractingEnergy")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercArmature/ROOT/base/stomach/chest/PreDashEffect/FireEmission")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercArmature/ROOT/base/stomach/chest/PreDashEffect/BrightRing")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercMesh")?.GetComponent<Renderer>(),
                modelRoot.transform.Find("MercSwordMesh")?.GetComponent<Renderer>(),
            ];

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
                RebakeSkinAsync(skin);
            }
        }

        public static void RebakeSkinAsync(SkinDef orig)
        {
            orig._runtimeSkin = null;

            SkinDefParams skinDefParams = orig.skinDefParams;
            skinDefParams.rendererInfos = ArrayUtils.Clone(orig.rendererInfos);
            skinDefParams.gameObjectActivations = new SkinDefParams.GameObjectActivation[orig.gameObjectActivations.Length];
            for (int i = 0; i < orig.gameObjectActivations.Length; i++)
            {
                skinDefParams.gameObjectActivations[i] = orig.gameObjectActivations[i];
            }

            skinDefParams.meshReplacements = new SkinDefParams.MeshReplacement[orig.meshReplacements.Length];
            for (int j = 0; j < orig.meshReplacements.Length; j++)
            {
                skinDefParams.meshReplacements[j] = orig.meshReplacements[j];
            }

            skinDefParams.projectileGhostReplacements = new SkinDefParams.ProjectileGhostReplacement[orig.projectileGhostReplacements.Length];
            for (int k = 0; k < orig.projectileGhostReplacements.Length; k++)
            {
                skinDefParams.projectileGhostReplacements[k] = orig.projectileGhostReplacements[k];
            }

            skinDefParams.minionSkinReplacements = new SkinDefParams.MinionSkinReplacement[orig.minionSkinReplacements.Length];
            for (int l = 0; l < orig.minionSkinReplacements.Length; l++)
            {
                skinDefParams.minionSkinReplacements[l] = orig.minionSkinReplacements[l];
            }


            // cursed
            var enumerator = orig.BakeAsync();
            while (enumerator.MoveNext()) ;
        }
#pragma warning restore CS0618 // Type or member is obsolete
    }
}
