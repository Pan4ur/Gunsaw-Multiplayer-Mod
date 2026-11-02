using UnityEngine;

internal static class LobbyAmmoRules
{
    internal const int MaxAmount = 999999;
    internal const string StartingDefault = "12;15;2;0";
    internal const string RespawnDefault = "30;60;15;3";

    private static readonly int[] ammoTypes = { 0, 1, 2, 4 };

    internal static int GetAmmoType(int index)
    {
        return index >= 0 && index < ammoTypes.Length ? ammoTypes[index] : -1;
    }

    internal static string WithAmount(string rule, int index, string value)
    {
        if (index < 0 || index >= ammoTypes.Length) throw new ArgumentOutOfRangeException(nameof(index));
        var values = (rule ?? string.Empty).Split(';').ToList();
        while (values.Count < ammoTypes.Length) values.Add("0");
        int.TryParse((value ?? string.Empty).Trim(), out var amount);
        values[index] = Mathf.Clamp(amount, 0, MaxAmount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return string.Join(";", values);
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
            body.ammoAmount[ammoTypes[index]] = Mathf.Clamp(amount, 0, MaxAmount);
        }

        if (PlayerScript.player != null && PlayerScript.player.bodyScript == body)
            PlayerScript.player.BodyAmmoChanged();
    }
}
