using UnityEngine;

[Serializable]
internal sealed class AmmoBoxData
{
    public int ammoType;
    public int amount = 30;
    public int oneTimeUse;
    public int cooldown = 30;
}

internal sealed class AmmoBoxPropDefinition : CustomPropDefinition<AmmoBoxData>
{
    private const string AmmoTypes = "0 = pistol, 1 = rifle, 2 = heavy, 3 = grenades";
    private CustomPropField[]? fields;

    public override string TypeId => "MP/AmmoBox";
    public override string DisplayName => "Ammo Box";
    public override string Description => "Refills reserve ammo. " + AmmoTypes;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.ammo-box.png", 28f, new Vector2(0.5f, 0.5f));

    public override CustomPropField[] Fields => fields ??= new []
    {
        Integer("Ammo type", AmmoTypes, value => value.ammoType, (value, number) => value.ammoType = Mathf.Min(number, 3), 0),
        Integer("Amount", "Rounds per use", value => value.amount, (value, number) => value.amount = Mathf.Min(number, LobbyAmmoRules.MaxAmount), 1),
        Integer("One time use", "0 - off, 1 - on per player", value => value.oneTimeUse, (value, number) => value.oneTimeUse = Mathf.Min(number, 1), 0),
        Integer("Cooldown", "Seconds", value => value.cooldown, (value, number) => value.cooldown = number, 0)
    };

    public override void CreateRuntime(GameObject gameObject, AmmoBoxData data)
    {
        CustomPropVisual.AddSprite(gameObject, Icon);
        gameObject.AddComponent<AmmoBoxRuntime>().Configure(data);
    }
}

internal sealed class AmmoBoxRuntime : StationRuntime
{
    private int ammoType;
    private int amount;

    protected override float PromptHeight => 0.8f;

    internal void Configure(AmmoBoxData data)
    {
        data ??= new AmmoBoxData();
        ammoType = LobbyAmmoRules.GetAmmoType(Mathf.Clamp(data.ammoType, 0, 3));
        amount = Mathf.Clamp(data.amount, 1, LobbyAmmoRules.MaxAmount);
        Configure(data.oneTimeUse > 0, data.cooldown);
    }

    protected override void OnUsed(BodyScript? body, bool local)
    {
        Sound.Play(Resources.Load<AudioClip>("Sounds/SmgReload1"), transform.position, twoDimensional: false, pitchShift: false);
        if (!local || body == null || !body.isAlive)
            return;

        body.ammoAmount ??= [];
        while (body.ammoAmount.Count <= ammoType) 
            body.ammoAmount.Add(0);
        
        body.ammoAmount[ammoType] = (int)Math.Min(int.MaxValue, (long)body.ammoAmount[ammoType] + amount);
        PlayerScript.player.BodyAmmoChanged();
    }
}