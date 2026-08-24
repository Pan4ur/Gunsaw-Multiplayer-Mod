using UnityEngine;

[Serializable]
internal sealed class RandomIdRouterData
{
    public int activationId;
    public string outputIds = "3,4,5";
}

internal sealed class RandomIdRouterPropDefinition : CustomPropDefinition<RandomIdRouterData>
{
    private CustomPropField[] fields;

    public override string TypeId => "MP/RND";
    public override string DisplayName => "RND";
    public override string Description => "Receives an activation signal and forwards it to one random ID from the list.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;

    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.rnd.png", 28f, new Vector2(0.5f, 0.15f));

    public override CustomPropField[] Fields
    {
        get
        {
            if (fields == null)
            {
                fields = new[]
                {
                    Integer(
                        "Activation ID",
                        "Input signal ID",
                        value => value.activationId,
                        (value, number) => value.activationId = number,
                        0),
                    Text(
                        "Random IDs",
                        "3,4,5",
                        value => value.outputIds,
                        (value, text) => value.outputIds = text)
                };
            }

            return fields;
        }
    }

    public override void CreateRuntime(GameObject gameObject, RandomIdRouterData data)
    {
        gameObject.AddComponent<RandomIdRouterRuntime>().Configure(data);
    }
}

internal sealed class RandomIdRouterRuntime : MonoBehaviour
{
    private RandomIdRouterData data;
    private bool routing;

    internal void Configure(RandomIdRouterData value)
    {
        data = value;
    }

    private void Activate(int value)
    {
        if (data == null || routing || value != data.activationId) return;
        
        var ids = ParseIds(data.outputIds);
        if (ids.Count == 0) return;

        var selectedId = ids[UnityEngine.Random.Range(0, ids.Count)];

        routing = true;
        try
        {
            foreach (var target in GameObject.FindGameObjectsWithTag("Activateable"))
            {
                if (target != null) target.SendMessage("Activate", selectedId, SendMessageOptions.DontRequireReceiver);
            }
        }
        finally
        {
            routing = false;
        }
    }

    private static List<int> ParseIds(string text)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var parts = text.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            int id;
            if (int.TryParse(part.Trim(), out id) && id >= 0) result.Add(id);
        }

        return result;
    }
}
