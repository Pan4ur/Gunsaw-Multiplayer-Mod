using UnityEngine;

internal static class LobbyAmmoRules
{
    internal const string StartingDefault = "12;15;2;0";
    internal const string RespawnDefault = "30;60;15;3";

    private static readonly int[] ammoTypes = { 0, 1, 2, 4 };

    internal static int GetAmmoType(int index)
    {
        return index >= 0 && index < ammoTypes.Length ? ammoTypes[index] : -1;
    }

    internal static void Apply(BodyScript body, string rule)
    {
        if (body == null) return;
        if (body.ammoAmount == null) body.ammoAmount = new List<int>();
        while (body.ammoAmount.Count < 5) body.ammoAmount.Add(0);

        var values = (rule ?? string.Empty).Split(';');
        for (var index = 0; index < ammoTypes.Length; index++)
        {
            var amount = 0;
            if (index < values.Length) int.TryParse(values[index].Trim(), out amount);
            body.ammoAmount[ammoTypes[index]] = Mathf.Clamp(amount, 0, 999999);
        }

        if (PlayerScript.player != null && PlayerScript.player.bodyScript == body)
            PlayerScript.player.BodyAmmoChanged();
    }
}
