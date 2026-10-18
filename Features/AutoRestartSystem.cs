using UnityEngine;
using UnityEngine.SceneManagement;

internal static class AutoRestartSystem
{
    private static float restartAt = -1f;
    private static int sceneHandle = int.MinValue;
    private static bool teamMatchStarted;

    internal static void Tick(bool enabled)
    {
        if (!enabled || !MultiplayerSession.IsHosting || GameManager.main == null || SceneManager.GetActiveScene().name == "LevelSelect")
        {
            restartAt = -1f;
            sceneHandle = int.MinValue;
            teamMatchStarted = false;
            return;
        }
        var currentScene = SceneManager.GetActiveScene().handle;
        if (sceneHandle != currentScene)
        {
            sceneHandle = currentScene;
            restartAt = -1f;
            teamMatchStarted = false;
        }
        if (!TeamSystem.Enabled) teamMatchStarted = false;

        var alivePlayers = 0;
        var aliveTeams = new HashSet<string>();
        var localBody = PlayerScript.player?.bodyScript;
        if (!HeadlessLobbyService.IsHeadlessServer && localBody != null)
        {
            if (localBody.isAlive)
            {
                alivePlayers++;
                if (TeamSystem.Enabled && !string.IsNullOrEmpty(TeamSystem.Name(MultiplayerSession.LocalPeerId)))
                    aliveTeams.Add(TeamSystem.Name(MultiplayerSession.LocalPeerId));
            }
        }
        foreach (var remote in NetworkAvatarManager.RemotePlayers())
        {
            if (remote.Body == null) continue;
            if (remote.Body.isAlive)
            {
                alivePlayers++;
                if (TeamSystem.Enabled && !string.IsNullOrEmpty(TeamSystem.Name(remote.PeerId)))
                    aliveTeams.Add(TeamSystem.Name(remote.PeerId));
            }
        }

        if (aliveTeams.Count > 1) teamMatchStarted = true;
        var restart = alivePlayers == 0 || (TeamSystem.Enabled && teamMatchStarted && aliveTeams.Count <= 1);
        
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
