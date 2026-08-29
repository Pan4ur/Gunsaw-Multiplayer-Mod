using UnityEngine;
using UnityEngine.SceneManagement;

internal static class AutoRestartSystem
{
    private static float restartAt = -1f;

    internal static void Tick(bool enabled)
    {
        if (!enabled || !MultiplayerSession.IsHosting || GameManager.main == null || SceneManager.GetActiveScene().name == "LevelSelect")
        {
            restartAt = -1f;
            return;
        }

        var alivePlayers = 0;
        var aliveTeams = new HashSet<string>();
        var localBody = PlayerScript.player?.bodyScript;
        if (!GunsawMultiplayerPlugin.IsHeadlessServer && localBody != null)
        {
            if (localBody.isAlive)
            {
                alivePlayers++;
                if (TeamSystem.Enabled) aliveTeams.Add(TeamSystem.Name(MultiplayerSession.LocalPeerId));
            }
        }
        foreach (var remote in NetworkAvatarRegistry.RemotePlayers())
        {
            if (remote.Body == null) continue;
            if (remote.Body.isAlive)
            {
                alivePlayers++;
                if (TeamSystem.Enabled) aliveTeams.Add(TeamSystem.Name(remote.PeerId));
            }
        }

        var restart = (alivePlayers == 0 || (TeamSystem.Enabled && aliveTeams.Count <= 1));
        
        if (!restart)
        {
            restartAt = -1f;
            return;
        }
        
        if (restartAt < 0f)
        {
            restartAt = Time.unscaledTime + (MultiplayerSession.AllowRespawn ? MultiplayerSession.RespawnTimeSeconds : 0f) + 3f;
            return;
        }
        
        if (Time.unscaledTime < restartAt)
            return;
        
        restartAt = -1f;
        ScoreboardSystem.PreserveForNextScene();
        MultiplayerSession.MarkNextSceneReloadAutoRestart();
        GameManager.main.Restart();
    }
}
