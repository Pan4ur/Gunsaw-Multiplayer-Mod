using UnityEngine;

[Serializable]
internal sealed class RestartLevelData
{
    public int activationId;
    public int mode;
}

internal sealed class RestartLevelDefinition : CustomPropDefinition<RestartLevelData>
{
    private CustomPropField[] fields;
    public override string TypeId => "MP/RestartLevel";
    public override string DisplayName => "Restart Level";
    public override string Description => "Restarts the level on activation. Mode 0 resets player scores; mode 1 keeps player scores.";
    public override CustomPropCategory EditorCategory => CustomPropCategory.Trigger;
    public override Sprite Icon => EmbeddedSpriteLoader.Load("GunsawMultiplayer.CustomProps.Assets.restart-level.png", 28f, new Vector2(0.5f, 0.15f));
    public override CustomPropField[] Fields => fields ??= new[]
    {
        Integer("Activation ID", "Input signal ID", value => value.activationId, (value, number) => value.activationId = number, 0),
        Integer("Mode", "0 = reset scores; 1 = keep scores", value => value.mode, (value, number) => value.mode = Mathf.Clamp(number, 0, 1), 0)
    };

    public override void CreateRuntime(GameObject gameObject, RestartLevelData data) =>
        gameObject.AddComponent<RestartLevelRuntime>().Configure(data);
}

internal sealed class RestartLevelRuntime : MonoBehaviour, IActivationIdReceiver
{
    private RestartLevelData data;
    private bool requested;
    public int ActivationId => data != null ? data.activationId : -1;

    internal void Configure(RestartLevelData value) => data = value;

    private void Activate(int id)
    {
        if (data != null && id == ActivationId && LogicTickService.CanSimulate)
            requested = true;
    }

    private void Update()
    {
        if (!requested || !LogicTickService.CanSimulate || SceneLoader.main == null || SceneLoader.main.isLoading || GameManager.main == null)
            return;
        
        requested = false;
        if (data.mode == 1)
        {
            ScoreboardSystem.PreserveForNextScene();
            MultiplayerSession.MarkNextSceneReloadAutoRestart();
        }
        GameManager.main.Restart();
    }
}