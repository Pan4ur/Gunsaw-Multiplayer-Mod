using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class GunGameRule
{
    private static string[] weapons = [];
    private static string sequence = "";
    private static BodyScript appliedBody;
    private static BodyScript lastDeathBody;
    private static int step;
    private static int scene = int.MinValue;

    internal static void Tick()
    {
        if (!MultiplayerSession.IsActive || !MultiplayerSession.GunGameEnabled || !MultiplayerSession.LobbySettingsReceived)
        {
            Reset();
            return;
        }

        var currentScene = SceneManager.GetActiveScene().handle;
        if (scene != currentScene || sequence != MultiplayerSession.GGSequence)
        {
            scene = currentScene;
            sequence = MultiplayerSession.GGSequence;
            weapons = sequence.Split(';').Select(name => name.Trim()).Where(name => name.Length > 0).ToArray();
            step = 0;
            appliedBody = null;
            lastDeathBody = null;
        }

        var body = PlayerScript.player?.bodyScript;
        if (body != null && body.isAlive && body != appliedBody && weapons.Length != 0)
            Apply(body);
    }

    internal static void RecordKill()
    {
        if (!MultiplayerSession.GunGameEnabled || weapons.Length == 0 || step >= weapons.Length - 1) return;
        step++;
        appliedBody = null;
        var body = PlayerScript.player?.bodyScript;
        if (body != null && body.isAlive) Apply(body);
    }

    internal static void RecordDeath(BodyScript body)
    {
        if (!MultiplayerSession.GunGameEnabled || body != PlayerScript.player?.bodyScript || body == lastDeathBody) return;
        lastDeathBody = body;
        step = MultiplayerSession.GGOnDeath switch
        {
            GunGameDeathMode.Reset => 0,
            GunGameDeathMode.Rollback => Math.Max(0, step - 1),
            _ => step
        };
        appliedBody = null;
    }

    internal static bool PickupKeyDown(KeyCode key) =>
        (!MultiplayerSession.IsActive || !MultiplayerSession.GunGameEnabled) && Input.GetKeyDown(key);

    private static void Apply(BodyScript body)
    {
        LocalPlayerReplication.EnsureRespawnWeaponSlots(body);
        for (var slot = 0; slot < body.weapons.Count; slot++)
        {
            body.weapons[slot] = null;
            body.weaponAmmos[slot] = 0;
        }
        LocalPlayerReplication.ApplyLobbyLoadout(body, weapons[step] + ";None;None");
        appliedBody = body;
    }

    private static void Reset()
    {
        weapons = [];
        sequence = "";
        appliedBody = null;
        lastDeathBody = null;
        step = 0;
        scene = int.MinValue;
    }
}

[HarmonyPatch(typeof(BodyWepPickup), "Update")]
internal static class GunGamePickupPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var pickupKey = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) });
        var guardedKey = AccessTools.Method(typeof(GunGameRule), nameof(GunGameRule.PickupKeyDown));
        foreach (var instruction in instructions)
        {
            if (Equals(instruction.operand, pickupKey)) instruction.operand = guardedKey;
            yield return instruction;
        }
    }
}
