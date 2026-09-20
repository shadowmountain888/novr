using HarmonyLib;
using Player = NuclearOption.Networking.Player;

namespace NOVR.HarmonyPatches;

/// <summary>
/// Holds the hosting player's faction funds at a configured floor, for stock missions whose
/// starting balance is baked into the game assets and cannot be edited like a user-authored
/// mission's startingBalance.
///
/// Same restriction as the rank override: hosting only, and only the local player's faction.
/// Funds are networked and spent through server-side Cmds, so on another player's server the host
/// decides what you can afford regardless of what the client reports.
/// </summary>
internal static class SinglePlayerFundsOverride
{
    [HarmonyPatch(typeof(FactionHQ), "get_factionFunds")]
    private static class FactionFundsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(FactionHQ __instance, ref float __result)
        {
            var minimumFunds = ModConfiguration.Instance?.SinglePlayerMinimumFunds.Value ?? 0f;
            if (minimumFunds <= 0f) return;

            if (__instance == null) return;
            if (!IsLocalHostedHq(__instance)) return;

            if (__result < minimumFunds)
            {
                __result = minimumFunds;
            }
        }

        private static bool IsLocalHostedHq(FactionHQ hq)
        {
            if (!GameManager.GetLocalPlayer<Player>(out var localPlayer)) return false;
            if (localPlayer == null || !localPlayer.IsHostPlayer) return false;

            if (!GameManager.GetLocalHQ(out var localHq)) return false;

            return localHq == hq;
        }
    }
}
