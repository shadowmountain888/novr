using HarmonyLib;
using UnityEngine;
using Player = NuclearOption.Networking.Player;

namespace NOVR.HarmonyPatches;

/// <summary>
/// Raises the local player's rank so rank-gated aircraft are selectable in stock single player
/// missions, where playerStartingRank is baked into the mission assets and cannot be edited the
/// way a user-authored mission can.
///
/// Deliberately limited to the hosting player. Rank lives on NuclearOption.Networking.Player and
/// every mutation path is a server-side Cmd (CmdPurchaseAirframe, CmdRequestReserveAirframe, ...),
/// so on someone else's server the host decides what you own regardless of what the client thinks.
/// Overriding there would only desync the UI from what the server will actually allow.
/// </summary>
internal static class SinglePlayerRankOverride
{
    [HarmonyPatch(typeof(Player), "get_PlayerRank")]
    private static class PlayerRankPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance, ref int __result)
        {
            var minimumRank = ModConfiguration.Instance?.SinglePlayerMinimumRank.Value ?? 0;
            if (minimumRank <= 0) return;

            if (__instance == null) return;

            // Hosting only - never while connected to another player's server.
            if (!__instance.IsHostPlayer) return;

            // Only the local player; other players' ranks stay exactly as the game reports them.
            if (!GameManager.IsLocalPlayer(__instance)) return;

            if (__result < minimumRank)
            {
                __result = minimumRank;
            }
        }
    }
}
