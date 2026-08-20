using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class CustomLevelProgress
{
    private static string activeKey = "";
    private static readonly Dictionary<string, string> catalogKeys = new Dictionary<string, string>();

    internal static void SetActive(string code) => activeKey = ScoreKeyForCatalogCode(code);
    internal static void SetActiveJson(string levelJson) => activeKey = GetHash(levelJson);
    internal static void ClearActive() => activeKey = "";
    internal static bool HasActive => !string.IsNullOrEmpty(activeKey);

    internal static string Rank(string code)
    {
        var score = PlayerPrefs.GetInt(ScoreKeyForCatalogCode(code));
        return score > 0 && score <= 7 ? MissionManager.IntToRank(score - 1) : "";
    }

    internal static Color RankColor(string code)
    {
        var score = PlayerPrefs.GetInt(ScoreKeyForCatalogCode(code));
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

    private static string GetHash(string code)
    {
        uint hash = 2166136261;
        foreach (var character in code ?? "") { hash ^= character; hash *= 16777619; } // TODO compat with https://github.com/rushellxyz/gunsaw-level-hashes/blob/main/hashes.json
        return "gunsawCustomLevel" + hash.ToString("X8") + "score";
    }

    private static string ScoreKeyForCatalogCode(string code)
    {
        string key;
        if (catalogKeys.TryGetValue(code ?? "", out key)) return key;
        try
        {
            var source = (code ?? "").Trim();
            if (!source.StartsWith("{", System.StringComparison.Ordinal))
            {
                using var compressed = new MemoryStream(System.Convert.FromBase64String(source));
                using var inflater = new DeflateStream(compressed, CompressionMode.Decompress);
                using var output = new MemoryStream();
                inflater.CopyTo(output);
                source = Encoding.UTF8.GetString(output.ToArray()).Trim();
            }
            key = GetHash(source);
        }
        catch { key = GetHash(code); }
        catalogKeys[code ?? ""] = key;
        return key;
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
