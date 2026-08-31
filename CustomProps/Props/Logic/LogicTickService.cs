using UnityEngine;

internal interface ILogicTickNode
{
    void LogicEvaluate();
}

internal sealed class LogicTickService : MonoBehaviour
{
    private static LogicTickService instance;
    private readonly List<ILogicTickNode> nodes = new List<ILogicTickNode>();
    private readonly HashSet<int> outputs = new HashSet<int>();
    private readonly List<ILogicTickNode> snapshot = new List<ILogicTickNode>();

    internal static bool CanSimulate => !MultiplayerSession.IsActive || MultiplayerSession.IsHost;

    internal static void ResetRuntime()
    {
        if (instance != null)
        {
            instance.enabled = false;
            instance.nodes.Clear();
            instance.outputs.Clear();
            instance.snapshot.Clear();
            Destroy(instance.gameObject);
        }
        instance = null;
    }

    internal static void Register(ILogicTickNode node)
    {
        if (node == null) return;
        var manager = Ensure();
        if (!manager.nodes.Contains(node)) manager.nodes.Add(node);
    }

    internal static void Unregister(ILogicTickNode node)
    {
        if (instance == null || node == null) 
            return;
        
        instance.nodes.Remove(node);
    }

    internal static void OutputHigh(int activationId)
    {
        if (activationId < 0 || !CanSimulate) 
            return;
        
        Ensure().outputs.Add(activationId);
    }

    private static LogicTickService Ensure()
    {
        if (instance != null) return instance;
        var gameObject = new GameObject("MP Logic Tick Manager");
        instance = gameObject.AddComponent<LogicTickService>();
        return instance;
    }

    private void FixedUpdate()
    {
        if (!CanSimulate)
        {
            outputs.Clear();
            return;
        }

        snapshot.Clear();
        snapshot.AddRange(nodes);
        foreach (var node in snapshot)
        {
            var behaviour = node as MonoBehaviour;
            
            if (node == null || behaviour == null || !behaviour.isActiveAndEnabled)
                continue;
            
            node.LogicEvaluate();
        }

        if (outputs.Count == 0) return;
        var targets = GameObject.FindGameObjectsWithTag("Activateable");
        foreach (var id in outputs)
        {
            for (var index = 0; index < targets.Length; index++)
            {
                var target = targets[index];
                if (target != null)
                    target.SendMessage("Activate", id, SendMessageOptions.DontRequireReceiver);
            }
        }
        outputs.Clear();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        nodes.Clear();
        outputs.Clear();
        snapshot.Clear();
    }
}

internal abstract class LogicRuntimeBase : MonoBehaviour, ILogicTickNode
{
    protected virtual void Awake()
    {
        LogicTickService.Register(this);
    }

    protected virtual void OnDestroy()
    {
        LogicTickService.Unregister(this);
    }

    public abstract void LogicEvaluate();
}
