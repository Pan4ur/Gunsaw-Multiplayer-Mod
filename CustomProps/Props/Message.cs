using UnityEngine;

[Serializable]
internal sealed class MessageData
{
    public int activationId;
    public string message = "";
}

internal sealed class MessageDefinition : CustomPropDefinition<MessageData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/Message";
    public override string DisplayName => "Message";
    public override string Description => "Sends the specified message to chat as SYSTEM on activation. Supports <color=red>text</color> tags.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.message.png", 28f, new Vector2(0.5f, 0.15f));
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Activation ID", "Input signal ID", value => value.activationId, (value, number) => value.activationId = number, 0),
        Text("Message", "System message text", value => value.message, (value, text) => value.message = text)
    };

    public override void CreateRuntime(GameObject gameObject, MessageData data) =>
        gameObject.AddComponent<MessageRuntime>().Configure(data);
}

internal sealed class MessageRuntime : MonoBehaviour, IActivationIdReceiver
{
    private MessageData data;
    public int ActivationId => data != null ? data.activationId : -1;

    internal void Configure(MessageData value) => data = value;

    private void Activate(int id)
    {
        if (data == null || id != ActivationId || !LogicTickService.CanSimulate || string.IsNullOrWhiteSpace(data.message))
            return;

        MultiplayerSession.BroadcastSystemChat(data.message, richText: true);
    }
}
