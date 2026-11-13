using HarmonyLib;
using UnityEngine;

internal sealed partial class MultiplayerHud
{
    [HarmonyPatch(typeof(Input))]
    internal static class ChatOpenHandler
    {
        [HarmonyPatch(nameof(Input.GetKey), typeof(KeyCode))]
        [HarmonyPrefix]
        private static bool PrefixGetKey(ref bool __result, KeyCode key)
        {
            if (IsChatOpenAndKeyIsTypable(key))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(nameof(Input.GetKeyDown), typeof(KeyCode))]
        [HarmonyPrefix]
        private static bool PrefixGetKeyDown(ref bool __result, KeyCode key)
        {
            if (IsChatOpenAndKeyIsTypable(key))
            {
                __result = false;
                return false;
            }
            return true;
        }

        [HarmonyPatch(nameof(Input.GetKeyUp), typeof(KeyCode))]
        [HarmonyPrefix]
        private static bool PrefixGetKeyUp(ref bool __result, KeyCode key)
        {
            if (IsChatOpenAndKeyIsTypable(key))
            {
                __result = false;
                return false;
            }
            return true;
        }

        private static bool IsChatOpenAndKeyIsTypable(KeyCode key)
        {
            return (null != MultiplayerHud.Instance && MultiplayerHud.Instance.ChatOpen) &&
                !readingChatInput &&
                ((KeyCode.Space == key) || (KeyCode.A <= key && KeyCode.Z >= key) || (KeyCode.Alpha0 <= key && KeyCode.Alpha9 >= key) ||
                 key == Controls.keys[Controls.CLOSE_CHAT]);
        }
    }
}