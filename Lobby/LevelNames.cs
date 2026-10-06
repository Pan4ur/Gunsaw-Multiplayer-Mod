using System.Security.Cryptography;
using System.Text;

internal static class LevelNames
{
    private const string HashesUrl = "https://raw.githubusercontent.com/rushellxyz/gunsaw-level-hashes/refs/heads/main/hashes.txt";
    private const string HashesPath = "hashes-to-name.txt";
    private static readonly object sync = new ();
    private static Dictionary<string, string>? hashesToName;
    private static bool downloading;
    private static string? lastCode;
    private static string lastHash = "";
    private static DateTime retryAfter;
    private static readonly Dictionary<string, string> levels = new ()
    {
        { "ViolenceWarning", "Just started" },
        { "tutorial1", "Basic Training" },
        { "actualLevel1", "Lock Break" },
        { "actualLevel2", "Box Check" },
        { "beautyLevel", "Belt Dropdown" },
        { "campaign3", "Box Check" },
        // Green skies is the only level with second word starting from small letter
        // You will never unsee this
        { "campaign4", "Green skies" },
        { "campaign5", "Zigzag" },
        { "campaign6", "Downdrops" },
        { "campaign7", "Crush Forces" },
        { "campaign8", "Mount Basins" },
        { "campaign9", "Blue Sewers" },
        { "campaign10", "Weird Technology" },
        { "campaign11", "Foggy Whites" },
        { "campaign12", "Rooftops" },
        { "campaign13", "Vanished Forts" },
        { "campaign14", "Acid Plants" },
        { "SampleScene", "Trash Containment" },
        { "LevelSelect", "Chooses level" },
        { "LevelEditor", "Level editor" },
        { "LevelLoader", "Custom level" },
        // Two secret levels, yep theres secret levels
        // try them with unity explorer
        { "level1", "Secret level" },
        { "level2", "Secret level" }
    };

    internal static string Resolve(string scene) => levels.TryGetValue(scene, out var level) ? level : scene;
    
    internal static string ResolveCustom(string levelCode, string fallback = "Custom level")
    {
        if (string.IsNullOrEmpty(levelCode)) return fallback;
        lock (sync)
        {
            if (lastCode != levelCode)
            {
                using (var sha256 = SHA256.Create())
                {
                    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(levelCode));
                    var hash = new StringBuilder(bytes.Length * 2);
                    foreach (var b in bytes) hash.Append(b.ToString("x2"));
                    lastHash = hash.ToString();
                }
                lastCode = levelCode;
            }
            if (hashesToName != null)
                return hashesToName.TryGetValue(lastHash, out var name) ? name : fallback;
            
            if (!downloading && DateTime.UtcNow >= retryAfter)
            {
                downloading = true;
                ThreadPool.QueueUserWorkItem(_ => Load());
            }
            
            return fallback;
        }
    }

    private static void Load()
    {
        try
        {
            var exists = File.Exists(HashesPath);
            if (!exists || File.GetLastWriteTimeUtc(HashesPath) < DateTime.UtcNow.AddDays(-1))
            {
                try
                {
                    using (var client = new HttpClient())
                        File.WriteAllBytes(HashesPath, client.GetByteArrayAsync(HashesUrl).GetAwaiter().GetResult());
                }
                catch (Exception exception)
                {
                    GunsawMultiplayerPlugin.LogInfo("Hashes to level name download failed: " + exception.Message);
                    if (!exists) return;
                }
            }
            var entries = File.ReadAllText(HashesPath).Split(',');
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i + 1 < entries.Length; i += 2)
            {
                var hash = entries[i].Trim();
                var name = entries[i + 1].Trim();
                if (hash.Length == 64 && name.Length != 0) names[hash] = name;
            }
            lock (sync) hashesToName = names;
        }
        catch (Exception exception)
        {
            GunsawMultiplayerPlugin.LogInfo("Hashes to level name loading failed: " + exception.Message);
        }
        finally
        {
            lock (sync)
            {
                downloading = false;
                retryAfter = DateTime.UtcNow.AddMinutes(1);
            }
        }
    }
}