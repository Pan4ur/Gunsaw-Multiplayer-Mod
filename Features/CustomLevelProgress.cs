using HarmonyLib;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class CustomLevelProgress
{
    private static string activeKey = "";

    internal static void SetActive(string code) => activeKey = ScoreKey(code);
    internal static void ClearActive() => activeKey = "";
    internal static bool HasActive => !string.IsNullOrEmpty(activeKey);

    internal static string Rank(string code)
    {
        var score = PlayerPrefs.GetInt(ScoreKey(code));
        return score > 0 && score <= 7 ? MissionManager.IntToRank(score - 1) : "";
    }

    internal static Color RankColor(string code)
    {
        var score = PlayerPrefs.GetInt(ScoreKey(code));
        return score switch
        {
            1 => new Color(0.8f, 0.25f, 0.25f),
            2 => new Color(0.9f, 0.5f, 0.2f),
            3 => new Color(0.95f, 0.82f, 0.2f),
            4 => new Color(0.3f, 0.78f, 0.45f),
            5 => new Color(0.28f, 0.75f, 0.95f),
            6 => new Color(0.78f, 0.42f, 0.95f),
            7 => Color.white,
            _ => new Color(1f, 1f, 1f, 0.25f)
        };
    }

    internal static void Record(string rank)
    {
        if (string.IsNullOrEmpty(activeKey)) return;
        var value = "DCBASUX".IndexOf(rank, System.StringComparison.Ordinal) + 1;
        if (value <= 0 || PlayerPrefs.GetInt(activeKey) >= value) return;
        PlayerPrefs.SetInt(activeKey, value);
        PlayerPrefs.Save();
    }

    private static string ScoreKey(string code) => GetHash(code) + "score";

    private static string GetHash(string code)
    {
        var bytes = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(code ?? ""));
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }
}

[HarmonyPatch(typeof(MissionManager), nameof(MissionManager.FinishMission))]
internal static class CustomLevelProgressPatch
{
    private static void Postfix(MissionManager __instance)
    {
        if (!CustomLevelProgress.HasActive || SceneManager.GetActiveScene().name != "LevelLoader" || __instance.finalRankText == null) return;
        CustomLevelProgress.Record(__instance.finalRankText.text);
    }
}
