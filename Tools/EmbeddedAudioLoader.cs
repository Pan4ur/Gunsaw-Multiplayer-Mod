using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

internal class EmbeddedAudioLoader
{
    private static readonly Dictionary<string, AudioClip> Cache = new();
    
    internal static AudioClip RespawnSound;

    public static void Init()
    {
        GunsawMultiplayerPlugin.Instance.StartCoroutine(Load("GunsawMultiplayer.Assets.Sounds.respawn.ogg", c => { RespawnSound = c; }));
    }

    private static IEnumerator Load(string name, Action<AudioClip> onLoaded)
    {
        if (Cache.TryGetValue(name, out var cached) && cached != null)
        {
            onLoaded?.Invoke(cached);
            yield break;
        }

        var assembly = Assembly.GetExecutingAssembly();

        byte[] bytes;

        using (var stream = assembly.GetManifestResourceStream(name))
        {
            if (stream == null)
            {
                Debug.LogError("[GunsawMP] Embedded audio not found: " + name);
                onLoaded?.Invoke(null);
                yield break;
            }

            bytes = new byte[stream.Length];

            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read <= 0) break;
                offset += read;
            }
        }

        string tempPath = Path.Combine(Path.GetTempPath(), "GunsawMultiplayer_" + name.GetHashCode() + ".ogg");

        File.WriteAllBytes(tempPath, bytes);

        string uri = new Uri(tempPath).AbsoluteUri;

        using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS))
        {
            yield return request.SendWebRequest();

            if (request.isNetworkError || request.isHttpError)
            {
                Debug.LogError("[GunsawMP] Failed to load audio: " + request.error);
                onLoaded?.Invoke(null);
                TryDelete(tempPath);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            clip.name = name;
            Cache[name] = clip;
            onLoaded?.Invoke(clip);
        }

        TryDelete(tempPath);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* holy inliners */ }
    }
}