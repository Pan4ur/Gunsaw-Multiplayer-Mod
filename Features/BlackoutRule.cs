using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;

internal static class BlackoutRule
{
    private static readonly Dictionary<Light2D, bool> disabledLights = new Dictionary<Light2D, bool>();
    private static readonly Dictionary<SpriteRenderer, Material> originalMaterials = new Dictionary<SpriteRenderer, Material>();
    public static Material litMaterial;
    private static bool applied;
    private static int appliedSceneHandle = int.MinValue;
    private static uint localSequence;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<IncomingHeadlamp> incomingHeadlamps = new System.Collections.Concurrent.ConcurrentQueue<IncomingHeadlamp>();
    private static readonly Dictionary<ushort, HeadlampPacket> headlampStates = new Dictionary<ushort, HeadlampPacket>();
    private static readonly HashSet<Headlamp> headlamps = new HashSet<Headlamp>();
    private static BodyScript localBodyWithHeadlamp;
    private static AudioClip headlampSwitchSound;

    internal static void Tick()
    {
        if (!MultiplayerSession.IsActive || !MultiplayerSession.BlackoutEnabled)
        {
            if (applied) RestoreLights();
            applied = false;
            return;
        }

        ProcessHeadlampPackets();
        RegisterRespawnedLocalBody();
        TickHeadlamps();
       
        var sceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        if (applied && sceneHandle == appliedSceneHandle)
            return;
        
        applied = true;
        appliedSceneHandle = sceneHandle;
        ApplyScene();
    }

    internal static void RegisterBody(BodyScript body)
    {
        if (!MultiplayerSession.IsActive || !MultiplayerSession.BlackoutEnabled || body == null || body.headTransform == null)
            return;
        
        EnsureHeadlamp(body);
        ApplyToObject(body.gameObject);
        DisableLights(body.gameObject);
        
        var ownerId = body == PlayerScript.player?.bodyScript ? MultiplayerSession.LocalPeerId : NetworkAvatarRegistry.ReplicaForBody(body)?.remotePeerId ?? 0;
        if (ownerId != 0 && headlampStates.TryGetValue(ownerId, out var state))
            body.headTransform.GetComponentInChildren<Headlamp>(true)?.SetState(state.Enabled, state.Sequence);
    }

    private static void ApplyScene()
    {
        foreach (var body in UnityEngine.Object.FindObjectsOfType<BodyScript>())
        {
            if (body == null || body.headTransform == null)
                continue;
            
            var localBody = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
            
            if (!body.isPlayer && body != localBody && body.GetComponentInParent<NetworkReplica>() == null)
                continue;
            
            EnsureHeadlamp(body);
        }
        
        ApplyLitMaterials();
        
        foreach (var light in UnityEngine.Object.FindObjectsOfType<Light2D>())
        {
            if (light == null || light.GetComponentInParent<Headlamp>() != null || light.GetComponentInParent<FireScript>() != null) 
                continue;
            
            if (!disabledLights.ContainsKey(light)) 
                disabledLights.Add(light, light.enabled);
            
            light.enabled = false;
        }
    }
    
    private static void EnsureHeadlamp(BodyScript body)
    {
        if (body.headTransform.GetComponentInChildren<Headlamp>(true) != null) return;
        var headlamp = new GameObject("MP Blackout Headlamp");
        headlamp.transform.SetParent(body.headTransform, false);
        headlamp.transform.localPosition = new Vector3(0f, 0.15f, 0f);
        var headlampController = headlamp.AddComponent<Headlamp>();
        var lightObject = new GameObject("Light");
        lightObject.transform.SetParent(headlamp.transform, false);
        lightObject.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
        var light = lightObject.AddComponent<Light2D>();
        light.enabled = false;
        light.lightType = Light2D.LightType.Point;
        light.pointLightInnerRadius = 0.4f;
        light.pointLightInnerAngle = 38f;
        light.pointLightOuterAngle = 64f;
        light.pointLightOuterRadius = 12f;
        var sortingLayerField = typeof(Light2D).GetField("m_ApplyToSortingLayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (sortingLayerField != null) sortingLayerField.SetValue(light, System.Array.ConvertAll(SortingLayer.layers, layer => layer.id));
        light.intensity = 2f;
        light.color = Color.white;
        light.enabled = true;
        var faceLightObject = new GameObject("MP Face Light");
        faceLightObject.transform.SetParent(headlamp.transform, false);
        var faceLight = faceLightObject.AddComponent<Light2D>();
        faceLight.enabled = false;
        faceLight.lightType = Light2D.LightType.Point;
        faceLight.pointLightInnerRadius = 0.1f;
        faceLight.pointLightOuterRadius = 0.8f;
        if (sortingLayerField != null) sortingLayerField.SetValue(faceLight, Array.ConvertAll(SortingLayer.layers, layer => layer.id));
        faceLight.intensity = 0.25f;
        faceLight.color = Color.white;
        faceLight.enabled = true;
        headlampController.Configure(light, faceLight);
        headlamps.Add(headlampController);
    }
    
    internal static Material GetLitMaterial()
    {
        if (litMaterial != null) return litMaterial;
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
        {
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null) continue;
            if (!renderer.sharedMaterial.shader.name.Contains("Sprite-Lit")) continue;
            litMaterial = renderer.sharedMaterial;
            return litMaterial;
        }
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (shader != null) litMaterial = new Material(shader);
        return litMaterial;
    }

    private static void ApplyLitMaterials()
    {
        var material = GetLitMaterial();
        if (material == null) return;
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
        {
            if (renderer == null || originalMaterials.ContainsKey(renderer)) continue;
            originalMaterials.Add(renderer, renderer.sharedMaterial);
            renderer.sharedMaterial = material;
        }
    }
    
    internal static void RegisterFire(FireScript fire)
    {
        if (!applied || fire == null || fire.GetComponentInChildren<BlackoutFireLight>(true) != null) return;
        var lightObject = new GameObject("MP Blackout Fire Light");
        lightObject.transform.SetParent(fire.transform, false);
        lightObject.AddComponent<BlackoutFireLight>();
        var light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.pointLightInnerRadius = 0.25f;
        light.pointLightOuterRadius = 3.5f;
        var sortingLayerField = typeof(Light2D).GetField("m_ApplyToSortingLayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (sortingLayerField != null) sortingLayerField.SetValue(light, System.Array.ConvertAll(SortingLayer.layers, layer => layer.id));
        light.intensity = 0.8f;
        light.color = new Color(1f, 0.36f, 0.12f);
    }

    internal static void ApplyToObject(GameObject gameObject)
    {
        if (!applied || gameObject == null) return;
        foreach (var renderer in gameObject.GetComponentsInChildren<SpriteRenderer>(true))
            ApplyToNewSpriteRenderer(renderer);
    }

    private static void DisableLights(GameObject gameObject)
    {
        if (gameObject == null) return;
        foreach (var light in gameObject.GetComponentsInChildren<Light2D>(true))
        {
            if (light == null || light.GetComponentInParent<Headlamp>() != null || light.GetComponentInParent<FireScript>() != null) continue;
            if (!disabledLights.ContainsKey(light)) disabledLights.Add(light, light.enabled);
            light.enabled = false;
        }
    }

    internal static void ApplyToNewSpriteRenderer(SpriteRenderer renderer)
    {
        if (!applied || renderer == null || litMaterial == null || originalMaterials.ContainsKey(renderer)) return;
        originalMaterials.Add(renderer, renderer.sharedMaterial);
        renderer.sharedMaterial = litMaterial;
    }
    
    internal static bool IsVisibleInHeadlamp(BodyScript body) => IsPositionIlluminated(body == null ? Vector2.zero : body.transform.position);

    internal static bool IsPositionIlluminated(Vector2 position)
    {
        if (!MultiplayerSession.BlackoutEnabled) return true;
        var localBody = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        var lamp = localBody == null ? null : localBody.headTransform.GetComponentInChildren<Headlamp>(true);
        return lamp != null && lamp.Illuminates(position);
    }
    
    private static void PlayHeadlampSwitchSound()
    {
        if (headlampSwitchSound == null) headlampSwitchSound = Resources.Load<AudioClip>("Sounds/revolverRel5");
        if (headlampSwitchSound != null) Sound.Play(headlampSwitchSound, Vector2.zero, twoDimensional: true, pitchShift: false, pitch: 8f);
    }

    internal static void HandleInput()
    {
        if (!MultiplayerSession.IsActive || !MultiplayerSession.BlackoutEnabled || !Input.GetKeyDown(Controls.keys[Controls.TOGGLE_HEADLAMP])) return;
        var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        var lamp = body == null ? null : body.headTransform.GetComponentInChildren<Headlamp>(true);
        if (lamp == null) return;
        var packet = new HeadlampPacket(MultiplayerSession.LocalPeerId, !lamp.Enabled, ++localSequence);
        headlampStates[packet.OwnerId] = packet;
        lamp.SetState(packet.Enabled, packet.Sequence);
        PlayHeadlampSwitchSound();
        MultiplayerSession.Send(packet);
    }

    internal static void ReceiveHeadlamp(ushort senderId, HeadlampPacket packet)
    {
        if (packet.OwnerId == 0) return;
        incomingHeadlamps.Enqueue(new IncomingHeadlamp(senderId, packet));
    }

    private static void ProcessHeadlampPackets()
    {
        while (incomingHeadlamps.TryDequeue(out var incoming))
        {
            var packet = incoming.Packet;
            if (MultiplayerSession.IsHost && incoming.SenderId != MultiplayerSession.LocalPeerId)
            {
                if (packet.OwnerId != incoming.SenderId) continue;
                MultiplayerSession.Send(packet);
            }
            headlampStates[packet.OwnerId] = packet;
            var body = packet.OwnerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : NetworkAvatarRegistry.RemoteBodyForPeer(packet.OwnerId);
            var lamp = body == null ? null : body.headTransform.GetComponentInChildren<Headlamp>(true);
            if (lamp != null) lamp.SetState(packet.Enabled, packet.Sequence);
        }
    }
    
    private static void RegisterRespawnedLocalBody()
    {
        var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        if (body == null || body == localBodyWithHeadlamp) return;
        RegisterBody(body);
        if (body.headTransform != null && body.headTransform.GetComponentInChildren<Headlamp>(true) != null)
            localBodyWithHeadlamp = body;
    }

    private static void TickHeadlamps()
    {
        headlamps.RemoveWhere(lamp => lamp == null);
        foreach (var lamp in headlamps) lamp.Tick();
    }

    private static void RestoreLights()
    {
        foreach (var pair in disabledLights)
            if (pair.Key != null) pair.Key.enabled = pair.Value;
        
        disabledLights.Clear();
        appliedSceneHandle = int.MinValue;
        headlampStates.Clear();
        headlamps.Clear();
        localBodyWithHeadlamp = null;
        
        foreach (var pair in originalMaterials)
            if (pair.Key != null) pair.Key.sharedMaterial = pair.Value;

        originalMaterials.Clear();
        
        foreach (var lamp in UnityEngine.Object.FindObjectsOfType<Headlamp>())
            if (lamp != null) UnityEngine.Object.Destroy(lamp.gameObject);
        
        foreach (var fireLight in UnityEngine.Object.FindObjectsOfType<BlackoutFireLight>())
            if (fireLight != null) UnityEngine.Object.Destroy(fireLight.gameObject);
    }
}

internal readonly struct IncomingHeadlamp
{
    internal readonly ushort SenderId;
    internal readonly HeadlampPacket Packet;

    internal IncomingHeadlamp(ushort senderId, HeadlampPacket packet)
    {
        SenderId = senderId; 
        Packet = packet;
    }
}

internal sealed class Headlamp : MonoBehaviour
{
    private Light2D mainLight;
    private Light2D faceLight;
    private float transitionStarted;
    private float fromMain;
    private float fromFace;
    private uint sequence;
    internal bool Enabled { get; private set; } = true;

    internal void Configure(Light2D main, Light2D face)
    {
        mainLight = main;
        faceLight = face;
    }

    internal void SetState(bool enabled, uint newSequence)
    {
        if (newSequence <= sequence) return;
        sequence = newSequence;
        Enabled = enabled;
        transitionStarted = Time.unscaledTime;
        fromMain = mainLight == null ? 0f : mainLight.intensity;
        fromFace = faceLight == null ? 0f : faceLight.intensity;
    }

    internal void Tick()
    {
        var elapsed = Time.unscaledTime - transitionStarted;
        var target = Enabled ? TurnOnAnimation(elapsed) : Mathf.Lerp(fromMain, 0f, Mathf.Clamp01(elapsed / 0.05f));
        var faceTarget = Enabled ? target * 0.11666667f : Mathf.Lerp(fromFace, 0f, Mathf.Clamp01(elapsed / 0.05f));
        if (mainLight != null) { mainLight.enabled = true; mainLight.intensity = target; }
        if (faceLight != null) { faceLight.enabled = true; faceLight.intensity = faceTarget; }
    }

    internal bool Illuminates(Vector2 position)
    {
        if (!Enabled || mainLight == null) return false;
        var offset = position - (Vector2)mainLight.transform.position;
        if (offset.sqrMagnitude > 144f || offset.sqrMagnitude < 0.01f) return offset.sqrMagnitude < 0.01f;
        return Vector2.Angle(mainLight.transform.up, offset) <= 32f;
    }
    
    private float TurnOnAnimation(float elapsed)
    {
        var phase = ((sequence * 1103515245u + 12345u) & 255u) / 255f;
        if (elapsed < 0.025f) return 0.15f + phase * 0.2f;
        if (elapsed < 0.055f) return 1.4f + phase * 0.6f;
        if (elapsed < 0.085f) return 0.05f;
        if (elapsed < 0.125f) return 1f + phase;
        if (elapsed < 0.18f) return 0.35f + phase * 0.3f;
        return 2f;
    }
}

internal sealed class BlackoutFireLight : MonoBehaviour { }

[HarmonyPatch(typeof(FireScript), "Awake")]
internal static class BlackoutFirePatch
{
    private static void Postfix(FireScript __instance) => BlackoutRule.RegisterFire(__instance);
}