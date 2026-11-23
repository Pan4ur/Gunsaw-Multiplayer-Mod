using System.Globalization;
using UnityEngine;

internal sealed class ChatCommandSystem
{
    private static readonly Dictionary<string, Func<string, bool>> commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/kill"] = message => message.Length == 5 && Kill(),
        ["/me"] = Emote,
        ["/spawn"] = Spawn,
        ["/swap"] = Swap,
        ["/tp"] = Teleport,
        ["/ban"] = Ban,
        ["/kick"] = Kick,
        ["/scale"] = Scale,
        ["/clearblood"] = _ => LevelCleanupSystem.Clear(),
        ["/changeteam"] = ChangeTeam,
        ["/balanceteams"] = BalanceTeams
    };

    internal static IEnumerable<string> CommandNames => commands.Keys;

    internal static void Register(string command, Action<string> handler)
    {
        if (string.IsNullOrEmpty(command) || command.Length < 2 || command[0] != '/' || command.Any(char.IsWhiteSpace))
            throw new ArgumentException("A slash command without whitespace is required.", nameof(command));
        
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));
        
        commands.Add(command, message =>
        {
            GunsawMultiplayer.MultiplayerApi.Invoke(command, () => handler(message.Substring(command.Length).Trim()));
            return true;
        });
    }

    internal bool TryHandle(string message)
    {
        if (!MultiplayerSession.IsInLobby || string.IsNullOrEmpty(message) || message[0] != '/')
            return false;

        var end = 1;
        while (end < message.Length && !char.IsWhiteSpace(message[end])) end++;
        return commands.TryGetValue(message.Substring(0, end), out var handler) && handler(message);
    }

    private static bool Emote(string message)
    {
        if (string.IsNullOrWhiteSpace(message.Substring(3)))
            MultiplayerHud.AddSystemMessage("Usage: /me <text>");
        else if (MultiplayerSession.IsHost)
            TryBroadcastActionRequest(MultiplayerSession.LocalPeerId, message);
        else if (MultiplayerSession.IsConnected && MultiplayerSession.HostPeerId != 0 && ChatService.TryCreate(message, false, out var packet))
            MultiplayerSession.Send(packet, MultiplayerSession.HostPeerId);
        return true;
    }

    private static bool ChangeTeam(string message)
    {
        if (!string.IsNullOrWhiteSpace(message.Substring(11)))
            MultiplayerHud.AddSystemMessage("Usage: /changeteam.");
        else if (!TeamSystem.Enabled)
            MultiplayerHud.AddSystemMessage("Teams are disabled in this lobby.");
        else
            TeamSystem.ChangeTeam();
        return true;
    }

    private static bool BalanceTeams(string message)
    {
        if (!MultiplayerSession.IsHost)
            MultiplayerHud.AddSystemMessage("Only the lobby host can use /balanceteams.");
        else if (!string.IsNullOrWhiteSpace(message.Substring(13)))
            MultiplayerHud.AddSystemMessage("Usage: /balanceteams.");
        else if (!TeamSystem.Enabled)
            MultiplayerHud.AddSystemMessage("Teams are disabled in this lobby.");
        else
        {
            TeamSystem.BalanceTeams();
            MultiplayerHud.AddSystemMessage("Teams balanced.");
        }
        return true;
    }

    internal static bool TryBroadcastActionRequest(ushort senderId, string message)
    {
        if (!MultiplayerSession.IsHost || senderId == 0 || string.IsNullOrWhiteSpace(message) || !IsCommand(message, "/me"))
            return false;

        var action = message.Substring(3).Trim();
        if (action.Length == 0)
            return true;

        var playerName = senderId == MultiplayerSession.LocalPeerId ? MultiplayerSession.LocalPlayerName : MultiplayerSession.PlayerName(senderId);

        if (ChatService.TryCreate(playerName + " " + action, true, out var packet))
        {
            MultiplayerHud.AddSystemMessage(packet.Text);
            MultiplayerSession.Send(packet);
        }
        return true;
    }

    private static bool Kill()
    {
        if (!LocalPlayerReplication.KillLocalPlayer(PlayerDeathCause.SelfKill))
            MultiplayerHud.AddSystemMessage("You are dead already.");

        return true;
    }

    private static bool Spawn(string message)
    {
        if (MultiplayerSession.PvpEnabled)
        {
            MultiplayerHud.AddSystemMessage("/spawn is disabled in PVP lobbies.");
            return true;
        }

        if (!string.IsNullOrWhiteSpace(message.Substring(6)))
        {
            MultiplayerHud.AddSystemMessage("Usage: /spawn.");
            return true;
        }

        var body = PlayerScript.player?.bodyScript;
        if (body == null || !body.isAlive)
        {
            MultiplayerHud.AddSystemMessage("You cannot use /spawn while dead.");
            return true;
        }

        if (!CustomLevelSpawnSelection.TryGetRandomSpawnPosition(out var position) &&
            (LocalPlayerReplication.Instance == null ||
             !LocalPlayerReplication.Instance.TryGetLocalSpawnPosition(out position)))
        {
            MultiplayerHud.AddSystemMessage("The map spawn point is not available yet.");
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
        MultiplayerHud.AddSystemMessage("Teleported to a map spawn point.");
        return true;
    }

    private static bool Swap(string message)
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.AllowSwap)
        {
            MultiplayerHud.AddSystemMessage("/swap is disabled in this lobby.");
            return true;
        }

        var character = message.Length > 5 ? message.Substring(5).Trim() : "";
        if (!LocalPlayerReplication.TrySetRespawnCharacter(character, out var characterName))
        {
            MultiplayerHud.AddSystemMessage("Usage: /swap <character name>");
            return true;
        }

        if (MultiplayerSession.IsHost)
            NetworkAvatarManager.BroadcastSwapAnnouncement(MultiplayerSession.LocalPlayerName, characterName);
        else if (MultiplayerSession.IsConnected && MultiplayerSession.HostPeerId != 0)
        {
            ChatPacket packet;
            if (ChatService.TryCreate("/swap " + characterName, false, out packet))
                MultiplayerSession.Send(packet, MultiplayerSession.HostPeerId);
        }

        return true;
    }

    private static bool Teleport(string message)
    {
        if (!MultiplayerSession.IsConnected)
            return true;

        if (MultiplayerSession.PvpEnabled)
        {
            MultiplayerHud.AddSystemMessage("/tp is disabled in PVP lobbies.");
            return true;
        }

        var playerName = message.Length > 3 ? message.Substring(3).Trim() : "";
        if (string.IsNullOrEmpty(playerName))
        {
            MultiplayerHud.AddSystemMessage("Usage: /tp <player name>");
            return true;
        }

        var targetPeerId = FindPeerId(playerName, true);
        if (targetPeerId == 0)
        {
            MultiplayerHud.AddSystemMessage("Player " + playerName + " is not in the lobby.");
            return true;
        }

        if (!MultiplayerSession.IsHost)
        {
            MultiplayerSession.Send(new TeleportRequestPacket(targetPeerId));
            MultiplayerHud.AddSystemMessage("Teleporting to " + playerName + "...");
            return true;
        }

        var target = targetPeerId == MultiplayerSession.LocalPeerId
            ? PlayerScript.player?.bodyScript
            : NetworkAvatarManager.RemoteBodyForPeer(targetPeerId);
        
        var local = PlayerScript.player?.bodyScript;
        if (target == null || !target.isAlive || local == null)
        {
            MultiplayerHud.AddSystemMessage("Player " + playerName + " is unavailable.");
            return true;
        }

        local.transform.position = CustomLevelSpawnSelection.RandomizePosition(target.transform.position);
        ResetTeleportVelocity(local);
        PlayTeleportEffect(local.transform.position);
        MultiplayerHud.AddSystemMessage("Teleported to " + playerName + ".");
        return true;
    }

    private static bool Ban(string message)
    {
        if (!GunsawMultiplayerPlugin.Instance.CanBanPlayers)
        {
            MultiplayerHud.AddSystemMessage("Only the lobby host can use /ban.");
            return true;
        }

        var playerName = message.Length > 4 ? message.Substring(4).Trim() : "";
        if (string.IsNullOrEmpty(playerName))
        {
            MultiplayerHud.AddSystemMessage("Usage: /ban <player name>");
            return true;
        }

        var peerId = FindPeerId(playerName, false);
        if (peerId == 0)
        {
            MultiplayerHud.AddSystemMessage("Player " + playerName + " is not in the lobby.");
            return true;
        }

        MultiplayerHud.AddSystemMessage("Eliminating " + playerName + "...");
        GunsawMultiplayerPlugin.Instance.BanPlayerFromCommand(playerName, peerId);
        return true;
    }

    private static bool Kick(string message)
    {
        if (!MultiplayerSession.IsHost)
        {
            MultiplayerHud.AddSystemMessage("Only the lobby host can use /kick.");
            return true;
        }

        var playerName = message.Substring(5).Trim();
        if (string.IsNullOrEmpty(playerName))
        {
            MultiplayerHud.AddSystemMessage("Usage: /kick <player name>");
            return true;
        }

        var peerId = FindPeerId(playerName, false);
        if (peerId == 0)
        {
            MultiplayerHud.AddSystemMessage("Player " + playerName + " is not in the lobby.");
            return true;
        }

        MultiplayerSession.KickPeer(peerId, playerName + " was kicked from the lobby.");
        MultiplayerHud.AddSystemMessage(playerName + " was kicked from the lobby.");
        return true;
    }

    private static bool Scale(string message)
    {
        if (MultiplayerSession.IsActive && !MultiplayerSession.AllowScaleChanging)
        {
            MultiplayerHud.AddSystemMessage("/scale is disabled in this lobby.");
            return true;
        }

        var value = message.Length > 6 ? message.Substring(6).Trim() : "";
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || AvatarScaleHandler.Incorrect(scale))
        {
            MultiplayerHud.AddSystemMessage("Usage: /scale <0.25-3.0>");
            return true;
        }

        var body = PlayerScript.player?.bodyScript;
        if (body == null || !body.isAlive)
        {
            MultiplayerHud.AddSystemMessage("You cannot use /scale while dead.");
            return true;
        }

        if (!AvatarScaleHandler.TrySet(body, scale))
        {
            MultiplayerHud.AddSystemMessage("Character scale is unavailable right now.");
            return true;
        }

        MultiplayerHud.AddSystemMessage("Character scale set to " + scale.ToString("0.##", CultureInfo.InvariantCulture) + ".");
        return true;
    }

    private static bool IsCommand(string message, string command) =>
         message.StartsWith(command, StringComparison.OrdinalIgnoreCase) && (message.Length == command.Length || char.IsWhiteSpace(message[command.Length]));

    private static ushort FindPeerId(string playerName, bool includeLocalPlayer)
    {
        if (includeLocalPlayer && string.Equals(MultiplayerSession.LocalPlayerName, playerName, StringComparison.OrdinalIgnoreCase))
            return MultiplayerSession.LocalPeerId;
        
        foreach (var peerId in MultiplayerSession.PeerIds())
            if (string.Equals(MultiplayerSession.PlayerName(peerId), playerName, StringComparison.OrdinalIgnoreCase))
                return peerId;
        
        return 0;
    }

    internal static void ResetTeleportVelocity(BodyScript body)
    {
        body.lastMoveDir = Vector2.zero;
        foreach (var rb in body.GetComponentsInChildren<Rigidbody2D>(true))
        {
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
    }

    private static void PlayTeleportEffect(Vector3 position)
    {
        ScreenFXManager.main?.Teleported();
        
        var sound = Resources.Load<AudioClip>("Sounds/Teleport");
        if (sound != null)
            Sound.Play(sound, position, false, false);
    }
}
