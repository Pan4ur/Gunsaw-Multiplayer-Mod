using HarmonyLib;
using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;
using UnityEngine.U2D;

internal static class BlackoutRule
{
    private static readonly Dictionary<Light2D, bool> disabledLights = new();
    private static readonly Dictionary<SpriteRenderer, Material> originalMaterials = new();
    private static readonly Dictionary<SpriteShapeRenderer, Material[]> originalGroundMaterials = new();
    private static readonly Dictionary<LineRenderer, Material> originalLineMaterials = new();
    private static Material litMaterial;
    private static AssetBundle headlampBundle;
    internal static Material headlampMaterial;
    private static bool triedHeadlampMaterial;
    private static readonly Vector4[] headlampShaderData = new Vector4[16];
    private static readonly Vector4[] headlampShaderSettings = new Vector4[16];
    private static readonly Vector4[] headlampShaderFaces = new Vector4[16];
    private const int HeadlampBlockerLimit = 64;
    private const int HeadlampBlockerDataWidth = HeadlampBlockerLimit * 2;
    private static readonly Color[] headlampShaderBlockers = new Color[16 * HeadlampBlockerDataWidth];
    private static readonly float[] headlampShaderBlockerCounts = new float[16];
    private static readonly float[] headlampShaderGroundBlockers = new float[16 * HeadlampBlockerLimit];
    private static Texture2D headlampShaderBlockerTexture;
    private static Material levitatorMaterial;
    private static bool applied;
    private static int appliedSceneHandle = int.MinValue;
    private static uint localSequence;
    private static readonly System.Collections.Concurrent.ConcurrentQueue<IncomingHeadlamp> incomingHeadlamps = new();
    private static readonly Dictionary<ushort, HeadlampPacket> headlampStates = new();
    private static readonly HashSet<Headlamp> headlamps = [];
    private static readonly HashSet<BlackoutShadowCaster> shadowCasters = [];
    private static BodyScript localBodyWithHeadlamp;
    private static bool restrictLightApplied;
    private static AudioClip headlampToggleSound;

    internal static void Tick()
    {
        HandleInput();
        if (!MultiplayerSession.IsActive || !MultiplayerSession.BlackoutEnabled)
        {
            if (applied) 
                RestoreLights();
            
            applied = false;
            return;
        }

        ProcessHeadlampPackets();
        RegisterRespawnedLocalBody();
        TickHeadlamps();
        UpdateRestrictLightShadows();
       
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
        restrictLightApplied = false;
        UpdateRestrictLightShadows();
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
    
    private static void UpdateRestrictLightShadows()
    {
        var enabled = MultiplayerSession.RestrictLightEnabled;
        if (enabled == restrictLightApplied) 
            return;
      
        restrictLightApplied = enabled;
       
        if (!enabled)
        {
            RemoveRestrictLightShadows();
            return;
        }
       
        foreach (var collider in UnityEngine.Object.FindObjectsOfType<Collider2D>())
        {
            if (collider == null || collider.isTrigger || IsChainlinkFence(collider) || collider.GetComponentInParent<BodyScript>() != null || collider.GetComponent<BlackoutShadowCaster>() != null) 
                continue;
          
            var bounds = collider.bounds;
            if (bounds.size.x <= 0.001f || bounds.size.y <= 0.001f) 
                continue;
           
            var shape = GetShadowCasterShape(collider, bounds);
            var caster = collider.gameObject.AddComponent<ShadowCaster2D>();
            caster.enabled = false;
            caster.useRendererSilhouette = false;
            caster.castsShadows = true;
            caster.selfShadows = false;
            caster.m_ShapePath = shape;
            caster.m_ShapePathHash = collider.GetInstanceID();
            caster.enabled = true;
            caster.enabled = false;
            caster.enabled = true;
            var marker = collider.gameObject.AddComponent<BlackoutShadowCaster>();
            shadowCasters.Add(marker);
        }
    }

    internal static bool IsChainlinkFence(Collider2D collider)
    {
        for (var current = collider == null ? null : collider.transform; current != null; current = current.parent)
            if (current.name.IndexOf("chainlink", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
     
        return false;
    }

    private static Vector3[] GetShadowCasterShape(Collider2D collider, Bounds bounds)
    {
        if (collider is PolygonCollider2D polygon && polygon.pathCount > 0)
            return Array.ConvertAll(polygon.GetPath(0), point => (Vector3)point);
       
        if (collider is EdgeCollider2D edge && edge.points.Length > 2)
            return Array.ConvertAll(edge.points, point => (Vector3)point);
        
        var transform = collider.transform;
        return new[]
        {
            transform.InverseTransformPoint(new Vector3(bounds.min.x, bounds.min.y, transform.position.z)),
            transform.InverseTransformPoint(new Vector3(bounds.min.x, bounds.max.y, transform.position.z)),
            transform.InverseTransformPoint(new Vector3(bounds.max.x, bounds.max.y, transform.position.z)),
            transform.InverseTransformPoint(new Vector3(bounds.max.x, bounds.min.y, transform.position.z))
        };
    }
    
    private static void RemoveRestrictLightShadows()
    {
        foreach (var marker in shadowCasters)
        {
            if (marker == null) 
                continue;
           
            var caster = marker.gameObject.GetComponent<ShadowCaster2D>();
            if (caster != null)
                UnityEngine.Object.Destroy(caster);
          
            UnityEngine.Object.Destroy(marker);
        }
    }
    
    private static void EnsureHeadlamp(BodyScript body)
    {
        if (body.headTransform.GetComponentInChildren<Headlamp>(true) != null)
            return;
        
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
        if (sortingLayerField != null) 
            sortingLayerField.SetValue(light, System.Array.ConvertAll(SortingLayer.layers, layer => layer.id));
      
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
        
        if (sortingLayerField != null)
            sortingLayerField.SetValue(faceLight, Array.ConvertAll(SortingLayer.layers, layer => layer.id));
      
        faceLight.intensity = 0.25f;
        faceLight.color = Color.white;
        faceLight.enabled = true;
        headlampController.Configure(light, faceLight);
        headlamps.Add(headlampController);
    }
    
    private static Material GetHeadlampMaterial()
    {
        if (triedHeadlampMaterial) 
            return headlampMaterial;
       
        triedHeadlampMaterial = true;
       
        var stream = typeof(BlackoutRule).Assembly.GetManifestResourceStream("GunsawMultiplayer.Assets.headlamp-light");
        if (stream == null) 
            return null;
      
        using (stream)
        {
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            headlampBundle = AssetBundle.LoadFromMemory(bytes);
        }
        
        if (headlampBundle == null) 
            return null;
        
        var materials = headlampBundle.LoadAllAssets<Material>();
      
        if (materials.Length > 0)
            headlampMaterial = materials[0];
      
        return headlampMaterial;
    }

    private static void UpdateHeadlampShader()
    {
        if (headlampMaterial == null)
            return;
       
        var count = 0;
       
        foreach (var lamp in headlamps)
        {
            if (lamp == null || !lamp.WriteShaderData(headlampShaderData, headlampShaderSettings, headlampShaderFaces, headlampShaderBlockers, headlampShaderBlockerCounts, headlampShaderGroundBlockers, count)) 
                continue;
           
            count++;
            if (count == 16)
                break;
        }
        
        Shader.SetGlobalInt("_HeadlampCount", count);
        Shader.SetGlobalVectorArray("_HeadlampData", headlampShaderData);
        Shader.SetGlobalVectorArray("_HeadlampSettings", headlampShaderSettings);
        Shader.SetGlobalVectorArray("_HeadlampFaces", headlampShaderFaces);
        
        if (headlampShaderBlockerTexture == null) 
            headlampShaderBlockerTexture = new Texture2D(HeadlampBlockerDataWidth, 16, TextureFormat.RGBAFloat, false, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
       
        headlampShaderBlockerTexture.SetPixels(headlampShaderBlockers);
        headlampShaderBlockerTexture.Apply(false, false);
        Shader.SetGlobalFloatArray("_HeadlampBlockerCounts", headlampShaderBlockerCounts);
        Shader.SetGlobalFloatArray("_HeadlampGroundBlockers", headlampShaderGroundBlockers);
        Shader.SetGlobalTexture("_HeadlampBlockers", headlampShaderBlockerTexture);

    }
    
    internal static Material GetLitMaterial()
    {
        if (litMaterial != null) 
            return litMaterial;
        
        var customMaterial = GetHeadlampMaterial();
       
        if (customMaterial != null)
        {
            litMaterial = customMaterial; 
            return litMaterial;
        }
       
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
        {
            if (renderer == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                continue;
           
            if (!renderer.sharedMaterial.shader.name.Contains("Sprite-Lit")) 
                continue;
           
            litMaterial = renderer.sharedMaterial;
            return litMaterial;
        }
       
        var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Lit-Default");
        if (shader != null)
            litMaterial = new Material(shader);
       
        return litMaterial;
    }

    private static void ApplyLitMaterials()
    {
        var material = GetLitMaterial();
        if (material == null)
            return;
        
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
        {
            if (renderer == null || originalMaterials.ContainsKey(renderer)) 
                continue;
            
            originalMaterials.Add(renderer, renderer.sharedMaterial);
            renderer.sharedMaterial = material;
        }

        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<SpriteShapeRenderer>())
        {
            if (renderer == null || originalGroundMaterials.ContainsKey(renderer))
                continue;
         
            var original = renderer.sharedMaterials;
            originalGroundMaterials.Add(renderer, original);
          
            if (original.Length == 0)
            {
                renderer.sharedMaterial = new Material(material);
                continue;
            }
            
            var replacements = new Material[original.Length];
            for (var index = 0; index < replacements.Length; index++)
                replacements[index] = new Material(material);
            renderer.sharedMaterials = replacements;
        }
        
        var localLevitator = PlayerScript.player == null ? null : PlayerScript.player.levitLine;
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<LineRenderer>())
        {
            if (renderer == null || renderer != localLevitator && renderer.gameObject.name.IndexOf("Levit", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            
            if (originalLineMaterials.ContainsKey(renderer)) 
                continue;
           
            originalLineMaterials.Add(renderer, renderer.sharedMaterial);
           
            if (levitatorMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default") ?? Shader.Find("Sprites/Default");
                if (shader != null) 
                    levitatorMaterial = new Material(shader);
            }
            
            if (levitatorMaterial != null) 
                renderer.sharedMaterial = levitatorMaterial;
        }
    }
    
    internal static void RegisterFire(FireScript fire)
    {
        if (!applied || fire == null || fire.GetComponentInChildren<BlackoutFireLight>(true) != null)
            return;
       
        var lightObject = new GameObject("MP Blackout Fire Light");
        lightObject.transform.SetParent(fire.transform, false);
        lightObject.AddComponent<BlackoutFireLight>();
        var light = lightObject.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.pointLightInnerRadius = 0.25f;
        light.pointLightOuterRadius = 3.5f;
       
        var sortingLayerField = typeof(Light2D).GetField("m_ApplyToSortingLayers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (sortingLayerField != null) 
            sortingLayerField.SetValue(light, System.Array.ConvertAll(SortingLayer.layers, layer => layer.id));
        
        light.intensity = 0.8f;
        light.color = new Color(1f, 0.36f, 0.12f);
    }

    internal static void ApplyToObject(GameObject gameObject)
    {
        if (!applied || gameObject == null)
            return;
        
        foreach (var r in gameObject.GetComponentsInChildren<SpriteRenderer>(true))
            ApplyToNewSpriteRenderer(r);
    }

    private static void DisableLights(GameObject gameObject)
    {
        if (gameObject == null) 
            return;
        
        foreach (var light in gameObject.GetComponentsInChildren<Light2D>(true))
        {
            if (light == null || light.GetComponentInParent<Headlamp>() != null || light.GetComponentInParent<FireScript>() != null) 
                continue;
           
            if (!disabledLights.ContainsKey(light))
                disabledLights.Add(light, light.enabled);
            
            light.enabled = false;
        }
    }

    internal static void ApplyToNewSpriteRenderer(SpriteRenderer renderer)
    {
        if (!applied || renderer == null || litMaterial == null || originalMaterials.ContainsKey(renderer))
            return;
        
        originalMaterials.Add(renderer, renderer.sharedMaterial);
        renderer.sharedMaterial = litMaterial;
    }
    
    internal static bool IsVisibleInHeadlamp(BodyScript body) => IsPositionIlluminated(body == null ? Vector2.zero : body.transform.position);

    internal static bool IsPositionIlluminated(Vector2 position)
    {
        if (!MultiplayerSession.BlackoutEnabled)
            return true;
        
        var localBody = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        var lamp = localBody == null ? null : localBody.headTransform.GetComponentInChildren<Headlamp>(true);
      
        return lamp != null && lamp.Illuminates(position);
    }
    
    private static void PlayHeadlampSwitchSound()
    {
        if (headlampToggleSound == null) 
            headlampToggleSound = Resources.Load<AudioClip>("Sounds/revolverRel5");
        
        if (headlampToggleSound != null) 
            Sound.Play(headlampToggleSound, Vector2.zero, twoDimensional: true, pitchShift: false, pitch: 8f);
    }

    private static void HandleInput()
    {
        if (!MultiplayerSession.IsActive || !MultiplayerSession.BlackoutEnabled || !Input.GetKeyDown(Controls.keys[Controls.TOGGLE_HEADLAMP]))
            return;
        
        var body = PlayerScript.player == null ? null : PlayerScript.player.bodyScript;
        var lamp = body == null ? null : body.headTransform.GetComponentInChildren<Headlamp>(true);
       
        if (lamp == null) 
            return;
        
        var packet = new HeadlampPacket(MultiplayerSession.LocalPeerId, !lamp.Enabled, ++localSequence);
        headlampStates[packet.OwnerId] = packet;
        lamp.SetState(packet.Enabled, packet.Sequence);
        PlayHeadlampSwitchSound();
        MultiplayerSession.Send(packet);
    }

    internal static void ReceiveHeadlamp(ushort senderId, HeadlampPacket packet)
    {
        if (packet.OwnerId != 0) 
            incomingHeadlamps.Enqueue(new IncomingHeadlamp(senderId, packet));
    }

    private static void ProcessHeadlampPackets()
    {
        while (incomingHeadlamps.TryDequeue(out var incoming))
        {
            var packet = incoming.Packet;
            if (MultiplayerSession.IsHost && incoming.SenderId != MultiplayerSession.LocalPeerId)
            {
                if (packet.OwnerId != incoming.SenderId)
                    continue;
               
                MultiplayerSession.Send(packet);
            }
            headlampStates[packet.OwnerId] = packet;
           
            var body = packet.OwnerId == MultiplayerSession.LocalPeerId ? PlayerScript.player?.bodyScript : NetworkAvatarRegistry.RemoteBodyForPeer(packet.OwnerId);
           
            var lamp = body == null ? null : body.headTransform.GetComponentInChildren<Headlamp>(true);
            if (lamp != null)
                lamp.SetState(packet.Enabled, packet.Sequence);
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
        
        foreach (var lamp in headlamps)
            lamp.Tick();
       
        UpdateHeadlampShader();
        ApplyLevitatorMaterials();
    }

    private static void ApplyLevitatorMaterials()
    {
        var localLevitator = PlayerScript.player == null ? null : PlayerScript.player.levitLine;
        foreach (var renderer in UnityEngine.Object.FindObjectsOfType<LineRenderer>())
        {
            if (renderer == null || renderer != localLevitator && renderer.gameObject.name.IndexOf("Levit", StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            
            if (!originalLineMaterials.ContainsKey(renderer))
                originalLineMaterials.Add(renderer, renderer.sharedMaterial);
           
            if (levitatorMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader != null)
                    levitatorMaterial = new Material(shader);
            }
            
            if (levitatorMaterial != null && renderer.sharedMaterial != levitatorMaterial)
                renderer.sharedMaterial = levitatorMaterial;
        }
    }
    
    private static void RestoreLights()
    {
        foreach (var pair in disabledLights)
            if (pair.Key != null) 
                pair.Key.enabled = pair.Value;
        
        disabledLights.Clear();
        appliedSceneHandle = int.MinValue;
        headlampStates.Clear();
        headlamps.Clear();
        restrictLightApplied = false;
        localBodyWithHeadlamp = null;
        
        foreach (var pair in originalMaterials)
            if (pair.Key != null)
                pair.Key.sharedMaterial = pair.Value;
        originalMaterials.Clear();
       
        foreach (var pair in originalGroundMaterials)
            if (pair.Key != null)
                pair.Key.sharedMaterials = pair.Value;
        originalGroundMaterials.Clear();
        
        foreach (var pair in originalLineMaterials)
            if (pair.Key != null)
                pair.Key.sharedMaterial = pair.Value;
        originalLineMaterials.Clear();
        
        foreach (var lamp in UnityEngine.Object.FindObjectsOfType<Headlamp>())
            if (lamp != null) 
                UnityEngine.Object.Destroy(lamp.gameObject);
        
        RemoveRestrictLightShadows();
        
        foreach (var fireLight in UnityEngine.Object.FindObjectsOfType<BlackoutFireLight>())
            if (fireLight != null) 
                UnityEngine.Object.Destroy(fireLight.gameObject);
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
    private const int RestrictedBlockerLimit = 64;
    private const int RestrictedBlockerDataWidth = RestrictedBlockerLimit * 2;
    private readonly Collider2D[] blockingColliders = new Collider2D[64];
    private readonly Vector4[] blockerFirstPoints = new Vector4[RestrictedBlockerLimit];
    private readonly Vector4[] blockerSecondPoints = new Vector4[RestrictedBlockerLimit];
    private readonly float[] blockerDistances = new float[RestrictedBlockerLimit];
    private readonly bool[] blockerGround = new bool[RestrictedBlockerLimit];
    private int blockerCount;
    private static readonly Dictionary<Collider2D, SpriteRenderer> colliderVisuals = new Dictionary<Collider2D, SpriteRenderer>();
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
        var useShader = BlackoutRule.headlampMaterial != null;
      
        if (mainLight != null)
        {
            mainLight.enabled = !useShader; 
            mainLight.intensity = target; 
            UpdateRestrictedShape();
        }

        if (faceLight != null)
        {
            faceLight.enabled = !useShader;
            faceLight.intensity = faceTarget;
        }
    }

    private void UpdateRestrictedShape()
    {
        if (mainLight == null) return;
        mainLight.shadowsEnabled = MultiplayerSession.RestrictLightEnabled;
        mainLight.shadowIntensity = MultiplayerSession.RestrictLightEnabled ? 1f : 0f;
        mainLight.lightType = Light2D.LightType.Point;
        mainLight.pointLightInnerAngle = 38f;
        mainLight.pointLightOuterAngle = 64f;
        blockerCount = 0;
        if (!MultiplayerSession.RestrictLightEnabled) return;
        var origin = (Vector2)mainLight.transform.position;
        var count = Physics2D.OverlapCircleNonAlloc(origin, mainLight.pointLightOuterRadius, blockingColliders);
        for (var index = 0; index < Mathf.Min(count, blockingColliders.Length); index++)
        {
            var collider = blockingColliders[index];
            if (collider == null || !collider.enabled || collider.isTrigger || BlackoutRule.IsChainlinkFence(collider) || collider.name.IndexOf("Lamp", System.StringComparison.OrdinalIgnoreCase) >= 0 || collider.GetComponentInParent<BodyScript>() != null) continue;
            var ground = IsGroundCollider(collider);
            if (!ground && collider.GetComponentInParent<Renderer>() == null && collider.GetComponentInChildren<Renderer>(true) == null) continue;
            var bounds = collider.bounds;
            if (!CanIlluminate(bounds) || (!ground && bounds.Contains(origin))) continue;
            if (ground)
            {
                if (AddGroundSegments(collider, origin)) continue;
            }
            GetColliderPoints(collider, bounds, out var firstPoints, out var secondPoints);
            AddBlocker(firstPoints, secondPoints, ((Vector2)bounds.ClosestPoint(origin) - origin).sqrMagnitude, false);
        }
    }
    
    private static bool IsGroundCollider(Collider2D collider) =>
        collider.gameObject.layer == LayerMask.NameToLayer("Ground") || collider.name.StartsWith("GroundShape", StringComparison.OrdinalIgnoreCase);

    private bool AddGroundSegments(Collider2D collider, Vector2 origin)
    {
        var added = false;
        if (collider is PolygonCollider2D polygon)
        {
            for (var path = 0; path < 1; path++)
            {
                var points = polygon.GetPath(path);
                for (var point = 0; point < points.Length; point++)
                {
                    AddGroundSegment(polygon.transform.TransformPoint(points[point]), polygon.transform.TransformPoint(points[(point + 1) % points.Length]), origin);
                    added = true;
                }
            }
        }
        else if (collider is EdgeCollider2D edge)
        {
            var points = edge.points;
            for (var point = 0; point + 1 < points.Length; point++)
            {
                AddGroundSegment(edge.transform.TransformPoint(points[point]), edge.transform.TransformPoint(points[point + 1]), origin);
                added = true;
            }
        }
        return added;
    }

    private void AddGroundSegment(Vector2 start, Vector2 end, Vector2 origin)
    {
        var segment = end - start;
        var lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.000001f) return;
        var first = new Vector4(start.x, start.y, end.x, end.y);
        var second = new Vector4(end.x, end.y, start.x, start.y);
        var nearest = start + segment * Mathf.Clamp01(Vector2.Dot(origin - start, segment) / lengthSquared);
        AddBlocker(first, second, (nearest - origin).sqrMagnitude * 0.25f, true);
    }

    private void AddBlocker(Vector4 firstPoints, Vector4 secondPoints, float distance, bool ground)
    {
        for (var existing = 0; existing < blockerCount; existing++)
        {
            var sameDirection = (new Vector2(blockerFirstPoints[existing].x, blockerFirstPoints[existing].y) - new Vector2(firstPoints.x, firstPoints.y)).sqrMagnitude < 0.000001f &&
                                (new Vector2(blockerFirstPoints[existing].z, blockerFirstPoints[existing].w) - new Vector2(firstPoints.z, firstPoints.w)).sqrMagnitude < 0.000001f;
            var reversed = (new Vector2(blockerFirstPoints[existing].x, blockerFirstPoints[existing].y) - new Vector2(firstPoints.z, firstPoints.w)).sqrMagnitude < 0.000001f &&
                           (new Vector2(blockerFirstPoints[existing].z, blockerFirstPoints[existing].w) - new Vector2(firstPoints.x, firstPoints.y)).sqrMagnitude < 0.000001f;
            if (sameDirection || reversed) return;
        }
        var insert = blockerCount;
        while (insert > 0 && blockerDistances[insert - 1] > distance) insert--;
        if (insert >= RestrictedBlockerLimit) return;
        var last = Mathf.Min(blockerCount, RestrictedBlockerLimit - 1);
        for (var move = last; move > insert; move--)
        {
            blockerDistances[move] = blockerDistances[move - 1];
            blockerFirstPoints[move] = blockerFirstPoints[move - 1];
            blockerSecondPoints[move] = blockerSecondPoints[move - 1];
            blockerGround[move] = blockerGround[move - 1];
        }
        blockerDistances[insert] = distance;
        blockerFirstPoints[insert] = firstPoints;
        blockerSecondPoints[insert] = secondPoints;
        blockerGround[insert] = ground;
        blockerCount = Mathf.Min(blockerCount + 1, RestrictedBlockerLimit);
    }

    private static void GetColliderPoints(Collider2D collider, Bounds bounds, out Vector4 first, out Vector4 second)
    {
        var tileVisual = collider.name.IndexOf("Tile", System.StringComparison.OrdinalIgnoreCase) >= 0 ? collider.GetComponent<SpriteRenderer>() ?? collider.GetComponentInParent<SpriteRenderer>() ?? collider.GetComponentInChildren<SpriteRenderer>(true) : null;
        
        if (tileVisual != null && tileVisual.sprite != null)
        {
            var half = tileVisual.size * 0.5f;
            var center = (Vector2)tileVisual.sprite.bounds.center;
            var transform = tileVisual.transform;
            var bottomLeft = transform.TransformPoint(center + new Vector2(-half.x, -half.y));
            var topLeft = transform.TransformPoint(center + new Vector2(-half.x, half.y));
            var topRight = transform.TransformPoint(center + new Vector2(half.x, half.y));
            var bottomRight = transform.TransformPoint(center + new Vector2(half.x, -half.y));
            first = new Vector4(bottomLeft.x, bottomLeft.y, topLeft.x, topLeft.y);
            second = new Vector4(topRight.x, topRight.y, bottomRight.x, bottomRight.y);
            return;
        }
       
        if (collider is BoxCollider2D box)
        {
            var mesh = box.CreateMesh(true, true);
            var vertices = mesh == null ? null : mesh.vertices;
            if (vertices != null)
            {
                var points = new Vector2[4];
                var count = 0;
               
                foreach (var vertex in vertices)
                {
                    var point = (Vector2)vertex;
                    var duplicate = false;
                    for (var index = 0; index < count; index++)
                        if ((points[index] - point).sqrMagnitude < 0.000001f) duplicate = true;
                    if (!duplicate && count < points.Length) points[count++] = point;
                }
                
                if (count == 4)
                {
                    var center = (points[0] + points[1] + points[2] + points[3]) * 0.25f;
                    for (var index = 1; index < count; index++)
                    {
                        var point = points[index];
                        var angle = Mathf.Atan2(point.y - center.y, point.x - center.x);
                        var previous = index - 1;
                        while (previous >= 0 && Mathf.Atan2(points[previous].y - center.y, points[previous].x - center.x) > angle)
                        {
                            points[previous + 1] = points[previous];
                            previous--;
                        }
                        points[previous + 1] = point;
                    }
                    first = new Vector4(points[0].x, points[0].y, points[1].x, points[1].y);
                    second = new Vector4(points[2].x, points[2].y, points[3].x, points[3].y);
                    Destroy(mesh);
                    return;
                }
            }
            
            if (mesh != null) 
                Destroy(mesh);
        }        
        
        if (!colliderVisuals.TryGetValue(collider, out var visual) || visual == null)
        {
            var largestArea = 0f;
            foreach (var sprite in collider.GetComponentsInParent<SpriteRenderer>(true))
            {
                if (sprite == null || sprite.sprite == null) continue;
                var area = sprite.bounds.size.sqrMagnitude;
                if (area <= largestArea) continue;
                largestArea = area;
                visual = sprite;
            }
            
            foreach (var sprite in collider.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sprite == null || sprite.sprite == null) continue;
                var area = sprite.bounds.size.sqrMagnitude;
                if (area <= largestArea) continue;
                largestArea = area;
                visual = sprite;
            }
            
            if (visual != null) 
                colliderVisuals[collider] = visual;
        }
        
        if (visual != null && visual.sprite != null)
        {
            var localBounds = visual.sprite.bounds;
            var transform = visual.transform;
            var bottomLeft = transform.TransformPoint(new Vector2(localBounds.min.x, localBounds.min.y));
            var topLeft = transform.TransformPoint(new Vector2(localBounds.min.x, localBounds.max.y));
            var topRight = transform.TransformPoint(new Vector2(localBounds.max.x, localBounds.max.y));
            var bottomRight = transform.TransformPoint(new Vector2(localBounds.max.x, localBounds.min.y));
            first = new Vector4(bottomLeft.x, bottomLeft.y, topLeft.x, topLeft.y);
            second = new Vector4(topRight.x, topRight.y, bottomRight.x, bottomRight.y);
            return;
        }
        
        first = new Vector4(bounds.min.x, bounds.min.y, bounds.min.x, bounds.max.y);
        second = new Vector4(bounds.max.x, bounds.max.y, bounds.max.x, bounds.min.y);
    }
    
    internal bool WriteShaderData(Vector4[] data, Vector4[] settings, Vector4[] faces, Color[] blockers, float[] blockerCounts, float[] groundBlockers, int index)
    {
        if (!Enabled || mainLight == null || mainLight.intensity <= 0.01f) return false;
        var direction = (Vector2)mainLight.transform.up;
        var position = (Vector2)mainLight.transform.position;
        data[index] = new Vector4(position.x, position.y, direction.x, direction.y);
        settings[index] = new Vector4(mainLight.intensity * 0.5f, mainLight.pointLightOuterRadius, mainLight.pointLightInnerAngle, mainLight.pointLightOuterAngle);
        var facePosition = faceLight == null ? position : (Vector2)faceLight.transform.position;
        faces[index] = new Vector4(facePosition.x, facePosition.y, faceLight == null ? 0f : faceLight.intensity * 0.5f, faceLight == null ? 0f : faceLight.pointLightOuterRadius);
        blockerCounts[index] = blockerCount;
        for (var blocker = 0; blocker < RestrictedBlockerLimit; blocker++)
        {
            var first = blocker < blockerCount ? blockerFirstPoints[blocker] : Vector4.zero;
            var second = blocker < blockerCount ? blockerSecondPoints[blocker] : Vector4.zero;
            blockers[index * RestrictedBlockerDataWidth + blocker * 2] = new Color(first.x, first.y, first.z, first.w);
            blockers[index * RestrictedBlockerDataWidth + blocker * 2 + 1] = new Color(second.x, second.y, second.z, second.w);
            groundBlockers[index * RestrictedBlockerLimit + blocker] = blocker < blockerCount && blockerGround[blocker] ? 1f : 0f;
        }
        return true;
    }
    
    internal bool Illuminates(Vector2 position)
    {
        if (!Enabled || mainLight == null) return false;
        var offset = position - (Vector2)mainLight.transform.position;
        if (offset.sqrMagnitude > 144f || offset.sqrMagnitude < 0.01f) return offset.sqrMagnitude < 0.01f;
        return Vector2.Angle(mainLight.transform.up, offset) <= 32f;
    }
    
    internal bool CanIlluminate(Bounds bounds)
    {
        if (!Enabled || mainLight == null || mainLight.intensity <= 0.01f) return false;
        var origin = (Vector2)mainLight.transform.position;
        var center = (Vector2)bounds.center;
        var radius = ((Vector2)bounds.extents).magnitude;
        var offset = center - origin;
        var distance = offset.magnitude;
        if (distance - radius > mainLight.pointLightOuterRadius) return false;
        if (distance <= radius) return true;
        var padding = Mathf.Asin(Mathf.Min(1f, radius / distance)) * Mathf.Rad2Deg;
        return Vector2.Angle(mainLight.transform.up, offset) <= mainLight.pointLightOuterAngle * 0.5f + padding;
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

internal sealed class BlackoutShadowCaster : MonoBehaviour { }