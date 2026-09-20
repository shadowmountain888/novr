using HarmonyLib;
using NuclearOption.Effects;

namespace NOVR.HarmonyPatches;

/// <summary>
/// Forces the tree draw range to zero. The stock graphics menu only offers a tree DISTANCE slider
/// (GraphicsMenu.treeDistanceSlider) with no off switch - unlike grass, which has a real toggle -
/// so the minimum slider position still draws nearby trees. Useful in VR where foliage is the
/// single biggest GPU cost per eye.
/// </summary>
internal static class TreeRenderingToggle
{
    [HarmonyPatch(typeof(DetailSettings), "get_TreeRangeMultiplier")]
    private static class TreeRangeMultiplierPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ref float __result)
        {
            if (ModConfiguration.Instance?.DisableTrees.Value != true) return;

            __result = 0f;
        }
    }
}
