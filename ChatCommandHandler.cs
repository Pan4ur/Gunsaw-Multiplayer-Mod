using System.Globalization;
using UnityEngine;

internal sealed class ChatCommandHandler
{
    private readonly GunsawMultiplayerPlugin plugin;

    internal ChatCommandHandler(GunsawMultiplayerPlugin plugin)
    {
        this.plugin = plugin;
    }

    internal bool TryHandle(string message)
    {
        if (string.Equals(message, "/kill", StringComparison.OrdinalIgnoreCase)) return Kill();
        if (IsCommand(message, "/spawn")) return Spawn(message);
        if (IsCommand(message, "/swap")) return Swap(message);
        if (IsCommand(message, "/tp")) return Teleport(message);
        if (IsCommand(message, "/ban")) return Ban(message);
        if (IsCommand(message, "/scale")) return Scale(message);
        return false;
    }

    private bool Kill()
    {
        if (!NetworkAvatarReplication.KillLocalPlayer(PlayerDeathCause.SelfKill))
            plugin.status = "You are dead already (maybe inside only?)";
        return true;
    }

    private bool Spawn(string message)
    {
        if (!string.IsNullOrWhiteSpace(message.Substring(6)))
        {
            plugin.status = "Usage: /spawn";
            return true;
        }
        var body = PlayerScript.player?.bodyScript;
        if (body == null || !body.isAlive)
        {
            plugin.status = "You cannot use /spawn while dead.";
            return true;
        }
        if (!CustomLevelSpawnSelection.TryGetRandomSpawnPosition(out var position) &&
            (NetworkAvatarReplication.Instance == null ||
             !NetworkAvatarReplication.Instance.TryGetLocalSpawnPosition(out position)))
        {
            plugin.status = "The map spawn point is not available yet.";
            return true;
        }
        body.transform.position = position;
        if (body.rb != null)
        {
            body.rb.position = position;
            body.rb.velocity = Vector2.zero;
            body.rb.angularVelocity = 0f;
        }
        PlayTeleportEffect(position);
        plugin.status = "Teleported to a map spawn point.";
        return true;
    }

    private bool Swap(string message)
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.AllowSwap)
        {
            plugin.status = "/swap is disabled in this lobby.";
            return true;
        }
        var character = message.Length > 5 ? message.Substring(5).Trim() : "";
        if (!NetworkAvatarReplication.TrySetPendingRespawnCharacter(character, out var characterName))
        {
            plugin.status = "Usage: /swap <character name>";
            return true;
        }
        if (MultiplayerSession.IsHost)
            NetworkAvatarReplication.BroadcastSwapAnnouncement(MultiplayerSession.LocalPlayerName, characterName);
        else
        {
            ChatPacket packet;
            if (ChatService.TryCreate("/swap " + characterName, false, out packet)) MultiplayerSession.Send(packet);
            plugin.status = "You will respawn as " + characterName + ".";
        }
        return true;
    }

    private bool Teleport(string message)
    {
        if (!MultiplayerSession.IsConnected)
        {
            plugin.status = "/tp is only available in a CO-OP lobby.";
            return true;
        }
        if (MultiplayerSession.PvpEnabled)
        {
            plugin.status = "/tp is disabled in PVP lobbies.";
            return true;
        }
        var playerName = message.Length > 3 ? message.Substring(3).Trim() : "";
        if (string.IsNullOrEmpty(playerName))
        {
            plugin.status = "Usage: /tp <player name>";
            return true;
        }
        var targetPeerId = FindPeerId(playerName, true);
        if (targetPeerId == 0)
        {
            plugin.status = "Player " + playerName + " is not in the lobby.";
            return true;
        }
        if (!MultiplayerSession.IsHost)
        {
            MultiplayerSession.Send(new TeleportRequestPacket(targetPeerId));
            plugin.status = "Teleporting to " + playerName + "...";
            return true;
        }
        var target = targetPeerId == MultiplayerSession.LocalPeerId
            ? PlayerScript.player?.bodyScript
            : NetworkAvatarRegistry.RemoteBodyForPeer(targetPeerId);
        var local = PlayerScript.player?.bodyScript;
        if (target == null || !target.isAlive || local == null)
        {
            plugin.status = "Player " + playerName + " is unavailable.";
            return true;
        }
        local.transform.position = target.transform.position;
        if (local.rb != null) { local.rb.velocity = Vector2.zero; local.rb.angularVelocity = 0f; }
        PlayTeleportEffect(local.transform.position);
        plugin.status = "Teleported to " + playerName + ".";
        return true;
    }

    private bool Ban(string message)
    {
        if (!plugin.CanBanPlayers)
        {
            plugin.status = "Only the lobby host can use /ban.";
            return true;
        }
        var playerName = message.Length > 4 ? message.Substring(4).Trim() : "";
        if (string.IsNullOrEmpty(playerName))
        {
            plugin.status = "Usage: /ban <player name>";
            return true;
        }
        var peerId = FindPeerId(playerName, false);
        if (peerId == 0)
        {
            plugin.status = "Player " + playerName + " is not in the lobby.";
            return true;
        }
        plugin.status = "Banning " + playerName + "...";
        plugin.BanPlayerFromCommand(playerName, peerId);
        return true;
    }

    private bool Scale(string message)
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.AllowScaleChanging)
        {
            plugin.status = "/scale is disabled in this lobby.";
            return true;
        }
        var value = message.Length > 6 ? message.Substring(6).Trim() : "";
        float scale;
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) ||
            float.IsNaN(scale) || float.IsInfinity(scale) || scale < CharacterScaleRules.Minimum || scale > CharacterScaleRules.Maximum)
        {
            plugin.status = "Usage: /scale <0.25-2.0>";
            return true;
        }

        var body = PlayerScript.player?.bodyScript;
        if (body == null || !body.isAlive)
        {
            plugin.status = "You cannot use /scale while dead.";
            return true;
        }
        if (!CharacterScaleRules.TrySet(body, scale))
        {
            plugin.status = "Character scale is unavailable right now.";
            return true;
        }
        plugin.status = "Character scale set to " + scale.ToString("0.##", CultureInfo.InvariantCulture) + ".";
        return true;
    }

    private static bool IsCommand(string message, string command)
    {
        return message.StartsWith(command, StringComparison.OrdinalIgnoreCase) &&
            (message.Length == command.Length || char.IsWhiteSpace(message[command.Length]));
    }

    private static ushort FindPeerId(string playerName, bool includeLocalPlayer)
    {
        if (includeLocalPlayer && string.Equals(MultiplayerSession.LocalPlayerName, playerName,
                StringComparison.OrdinalIgnoreCase)) return MultiplayerSession.LocalPeerId;
        foreach (var peerId in MultiplayerSession.PeerIds())
            if (string.Equals(MultiplayerSession.PlayerName(peerId), playerName,
                    StringComparison.OrdinalIgnoreCase)) return peerId;
        return 0;
    }

    private static void PlayTeleportEffect(Vector3 position)
    {
        if (ScreenFXManager.main != null) ScreenFXManager.main.Teleported();
        var sound = Resources.Load<AudioClip>("Sounds/Teleport");
        if (sound != null) Sound.Play(sound, position, false, false);
    }
}
