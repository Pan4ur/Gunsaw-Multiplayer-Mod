using UnityEngine;

internal interface ILogicTickNode
{
    void LogicEvaluate();
}

internal interface IActivationIdReceiver
{
    int ActivationId { get; }
}

internal sealed class LogicTickService : MonoBehaviour
{
    private static LogicTickService instance;
    private readonly List<ILogicTickNode> nodes = new List<ILogicTickNode>();
    private readonly HashSet<int> outputs = new HashSet<int>();
    private readonly List<ILogicTickNode> snapshot = new List<ILogicTickNode>();
    private readonly Dictionary<int, List<ActivationReceiver>> receivers = new Dictionary<int, List<ActivationReceiver>>();
    private readonly Stack<List<ActivationReceiver>> receiverLists = new Stack<List<ActivationReceiver>>();
    private readonly List<Component> components = new List<Component>();
    private readonly List<int> fallbackTargets = new List<int>();
    private bool routeAsMessage;

    private readonly struct ActivationReceiver
    {
        internal readonly LogicRuntimeBase Node;
        internal readonly int TargetIndex;

        internal ActivationReceiver(LogicRuntimeBase node, int targetIndex)
        {
            Node = node;
            TargetIndex = targetIndex;
        }
    }

    internal static bool CanSimulate => !MultiplayerSession.IsActive || MultiplayerSession.IsHost;

    internal static void ResetRuntime()
    {
        if (instance != null)
        {
            instance.enabled = false;
            instance.nodes.Clear();
            instance.outputs.Clear();
            instance.snapshot.Clear();
            instance.ClearActivationRoutes();
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
        BuildActivationRoutes(targets);
        foreach (var id in outputs)
        {
            receivers.TryGetValue(id, out var routed);
            var receiverIndex = 0;
            var fallbackIndex = 0;
            object? boxedId = null;
            while (receiverIndex < (routed?.Count ?? 0) || fallbackIndex < fallbackTargets.Count)
            {
                if (receiverIndex < (routed?.Count ?? 0) &&
                    (fallbackIndex >= fallbackTargets.Count || routed[receiverIndex].TargetIndex < fallbackTargets[fallbackIndex]))
                {
                    var receiver = routed[receiverIndex++];
                    var target = targets[receiver.TargetIndex];
                    if (target == null) continue;
                    if (ReferenceEquals(receiver.Node, null))
                    {
                        if (boxedId == null) boxedId = id;
                        target.SendMessage("Activate", boxedId, SendMessageOptions.DontRequireReceiver);
                    }
                    else if (receiver.Node != null && target.activeInHierarchy)
                        receiver.Node.ReceiveActivation(id);
                }
                else
                {
                    var target = targets[fallbackTargets[fallbackIndex++]];
                    if (target != null)
                    {
                        if (boxedId == null) boxedId = id;
                        target.SendMessage("Activate", boxedId, SendMessageOptions.DontRequireReceiver);
                    }
                }
            }
        }
        outputs.Clear();
    }

    internal void AddActivationReceiver(int id, LogicRuntimeBase node, int targetIndex)
    {
        if (id < 0) return;
        if (routeAsMessage) node = null;
        if (!receivers.TryGetValue(id, out var list))
        {
            list = receiverLists.Count > 0 ? receiverLists.Pop() : new List<ActivationReceiver>();
            receivers.Add(id, list);
        }
        if (list.Count > 0 && list[list.Count - 1].TargetIndex == targetIndex &&
            ReferenceEquals(list[list.Count - 1].Node, node)) return;
        list.Add(new ActivationReceiver(node, targetIndex));
    }

    private void BuildActivationRoutes(GameObject[] targets)
    {
        ClearActivationRoutes();
        for (var index = 0; index < targets.Length; index++)
        {
            var target = targets[index];
            if (target == null) continue;
            target.GetComponents(components);
            var knownOnly = true;
            routeAsMessage = false;
            foreach (var component in components)
            {
                if (component is IActivationIdReceiver)
                {
                    routeAsMessage = true;
                    continue;
                }
                if (!(component is Transform) && !(component is LogicRuntimeBase))
                {
                    knownOnly = false;
                    break;
                }
            }
            if (!knownOnly)
            {
                fallbackTargets.Add(index);
                continue;
            }
            foreach (var component in components)
            {
                if (component is LogicRuntimeBase node) node.AddActivationRoutes(this, index);
                else if (component is IActivationIdReceiver receiver)
                    AddActivationReceiver(receiver.ActivationId, null, index);
            }
        }
        routeAsMessage = false;
        components.Clear();
    }

    private void ClearActivationRoutes()
    {
        foreach (var list in receivers.Values)
        {
            list.Clear();
            receiverLists.Push(list);
        }
        receivers.Clear();
        fallbackTargets.Clear();
        components.Clear();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        nodes.Clear();
        outputs.Clear();
        snapshot.Clear();
        ClearActivationRoutes();
        receiverLists.Clear();
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

    internal virtual void AddActivationRoutes(LogicTickService service, int targetIndex) { }

    internal virtual void ReceiveActivation(int id) { }
}
