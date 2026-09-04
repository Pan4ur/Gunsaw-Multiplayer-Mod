internal static class PlayerGruntService
{
    internal static void TryPlayLocal()
    {
        if (!MultiplayerSession.IsConnected) 
            return;
        
        var body = PlayerScript.player?.bodyScript;
        if (body == null)
            return;
        
        Play(body);
        MultiplayerSession.Send(new PlayerGruntPacket());
    }

    internal static void Tick()
    {
        ushort peerId;
        while (MultiplayerSession.TryTakePlayerGrunt(out peerId))
        {
            var body = NetworkAvatarRegistry.RemoteBodyForPeer(peerId);
            Play(body);
        }
    }

    private static void Play(BodyScript body)
    {
        if (body == null || GameManager.main == null || !GameManager.main.whinesEnabled || body.painNoises == null || body.painNoises.Count == 0) 
            return;
        
        var head = body.headTransform == null ? body.transform : body.headTransform;
        var sound = body.painNoises[UnityEngine.Random.Range(0, body.painNoises.Count)];
        
        if (sound != null) 
            Sound.Play(sound, head.position, false, false, head, 1f, body.voicePitch);
    }
}
