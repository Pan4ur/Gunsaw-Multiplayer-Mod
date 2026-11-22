using UnityEngine;
using UnityEngine.Experimental.Rendering.Universal;

[Serializable]
internal sealed class MedicalStationData
{
    public int oneTimeUse;
    public int cooldown = 30;
}

internal sealed class MedicalStationPropDefinition : CustomPropDefinition<MedicalStationData>
{
    private CustomPropField[] fields;

    public override string TypeId => "MP/MedicalStation";
    public override string DisplayName => "Medical Station";
    public override string Description => "Fully heals and extinguishes a player. One time use 1 - on, 0 - off; cooldown in seconds";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Misc;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.medical-station.png", 28f, new Vector2(0.5f, 0.5f));
  
    // it needs something like Supress
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("One time use", "0 - off, 1 - on", value => value.oneTimeUse, (value, number) => value.oneTimeUse = Mathf.Clamp(number, 0, 1), 0),
        Integer("Cooldown", "Seconds", value => value.cooldown, (value, number) => value.cooldown = number, 0)
    };

    public override void CreateRuntime(GameObject gameObject, MedicalStationData data)
    {
        CustomPropVisual.AddSprite(gameObject, Icon);
        gameObject.AddComponent<MedicalStationRuntime>().Configure(data);
    }
}

internal sealed class MedicalStationRuntime : StationRuntime
{
    private static readonly AnimationCurve screenPosition = new(
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(0.8333333f, 0.5069f, 0.5350666f, 0.5350666f),
        new Keyframe(1.5f, 0.8026f, 0f, 0f),
        new Keyframe(3.0833333f, 0.7952f, -0.01145812f, -0.01145812f),
        new Keyframe(4.0833335f, 0f, 0f, 0f));
    private static readonly AnimationCurve screenScale = new(
        new Keyframe(0f, 1f, 0f, 0f),
        new Keyframe(0.8333333f, 0.40858227f, -0.6242286f, -0.6242286f),
        new Keyframe(1.5f, 0.06365712f, 0f, 0f),
        new Keyframe(3.0833333f, 0.072244465f, 0.013296533f, 0.013296533f),
        new Keyframe(4.0833335f, 1f, 0f, 0f));
   
    private bool animating;
    private float usedAt;
    private Transform screen;
    private BodyScript? recipient;
    private LineRenderer bloodline, airLine;
    private readonly List<Light2D> lights = new();
    private Material tubeMaterial;

    internal void Configure(MedicalStationData value)
    {
        value ??= new MedicalStationData();
        Configure(value.oneTimeUse > 0, value.cooldown);
        var screenObject = new GameObject("Screen");
        screenObject.transform.SetParent(transform, false);
        screen = screenObject.transform;
        var sprite = EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.medical-station-screen.png", 28f, new Vector2(0.5f, 1f / 6f));
        CustomPropVisual.AddSprite(screenObject, sprite, 1000);
        AddLight(new Vector2(0f, 0.588f), Color.green);
        AddLight(new Vector2(-0.6422f, -0.8577f), Color.red);
        AddLight(new Vector2(0.6435f, -0.8577f), Color.red);
    }

    private void AddLight(Vector2 position, Color color)
    {
        var child = new GameObject("Station Light");
        child.transform.SetParent(transform, false);
        child.transform.localPosition = position * 0.5f;
        var light = child.AddComponent<Light2D>();
        light.lightType = Light2D.LightType.Point;
        light.pointLightOuterRadius = 1f;
        light.intensity = 0.5f;
        light.color = color;
        lights.Add(light);
    }

    protected override void Update()
    {
        foreach (var light in lights) light.enabled = AvailableFor(MultiplayerSession.LocalPeerId);
        if (animating)
        {
            var elapsed = Time.time - usedAt;
            screen.localPosition = new Vector3(screenPosition.Evaluate(elapsed) * 0.5f, 0f, 0f);
            screen.localScale = new Vector3(screenScale.Evaluate(elapsed), 1f, 1f);
            if (bloodline != null)
            {
                var active = recipient != null && elapsed < 3.8f;
                bloodline.enabled = airLine.enabled = active;
                if (active)
                {
                    bloodline.SetPosition(0, transform.TransformPoint(new Vector3(-0.0015f, 0.1765f, 0f)));
                    bloodline.SetPosition(1, recipient.transform.position);
                    airLine.SetPosition(0, transform.TransformPoint(new Vector3(-0.0015f, 0.3875f, 0f)));
                    airLine.SetPosition(1, recipient.headTransform.position);
                }
            }
            if (elapsed > 4.1f) animating = false;
        }
        base.Update();
    }

    protected override void OnUsed(BodyScript? body, bool local)
    {
        animating = true;
        usedAt = Time.time;

        if (local && body != null && body.isAlive)
        {
            LobbyHealthRule.RestoreFull(body);
            body.burnIntensity = 0f;
            body.shockTime = 0f;
            body.fireImmunityTime = Mathf.Max(body.fireImmunityTime, 5f);
           
            foreach (var limb in body.limbs)
                if (limb != null)
                {
                    if (!limb.dismembered && limb.passer != null && limb.passer.relevantDismember != null)
                        limb.passer.relevantDismember.currentDamage = 0f;
                    
                    foreach (var fire in limb.GetComponentsInChildren<FireScript>())
                    {
                        fire.canIgnite = false;
                        fire.damageMult = 0f;
                        fire.fuel = -1f;
                    }
                }
        }
        
        recipient = body;
        
        if (body != null)
        {
            if (tubeMaterial == null)
            {
                tubeMaterial = new Material(Shader.Find("Sprites/Default"));
                bloodline = CreateTube("Bloodline", new Color(0.5f, 0f, 0f));
                airLine = CreateTube("Air Line", Color.white);
            }
        }
        
        if (EmbeddedAudioLoader.MedicalStationSound != null)
            Sound.Play(EmbeddedAudioLoader.MedicalStationSound, transform.position, false, false);
    }

    private LineRenderer CreateTube(string name, Color color)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        var line = child.AddComponent<LineRenderer>();
        line.sharedMaterial = tubeMaterial;
        line.positionCount = 2;
        line.startWidth = line.endWidth = 0.11393f;
        line.startColor = line.endColor = color;
        line.numCapVertices = 3;
        line.sortingOrder = 500;
        return line;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (tubeMaterial != null)
            Destroy(tubeMaterial);
    }
}
