using UnityEngine;

[Serializable]
internal sealed class HubData
{
    public int activationId;
    public string outputIds = "3,4,5";
}

internal sealed class HubDefinition : CustomPropDefinition<HubData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Hub";
    public override string DisplayName => "Hub";
    public override string Description => "Copies every input signal to all output IDs, separated by commas or semicolons.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.hub.png", 28f, new Vector2(0.5f, 0.15f));
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Activation ID", "Input signal ID", value => value.activationId, (value, number) => value.activationId = number, 0),
        Text("Output IDs", "3,4,5 or 3;4;5", value => value.outputIds, (value, text) => value.outputIds = text)
    };

    public override void CreateRuntime(GameObject gameObject, HubData data) =>
        gameObject.AddComponent<HubRuntime>().Configure(data);
}

internal sealed class HubRuntime : MonoBehaviour, IActivationIdReceiver
{
    private HubData data;
    private int[] outputs = Array.Empty<int>();
    private bool routing;
    public int ActivationId => data != null ? data.activationId : -1;

    internal void Configure(HubData value)
    {
        data = value;
        outputs = RandomIdRouterRuntime.ParseIds(value.outputIds).Distinct().ToArray();
    }

    private void Activate(int id)
    {
        if (data == null || routing || id != ActivationId || outputs.Length == 0 || !LogicTickService.CanSimulate) return;
        routing = true;
        try
        {
            var targets = GameObject.FindGameObjectsWithTag("Activateable");
            foreach (var output in outputs)
                foreach (var target in targets)
                    target.Let(it => it.SendMessage("Activate", output, SendMessageOptions.DontRequireReceiver));
        }
        finally
        {
            routing = false;
        }
    }
}