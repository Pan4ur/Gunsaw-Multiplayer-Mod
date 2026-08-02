using System;
using UnityEngine;

[Serializable]
internal sealed class CustomPropPayload
{
    public int version = 1;
    public string uid;
    public string type;
    public string data;
}

internal sealed class CustomPropInstance
{
    internal string Uid;
    internal string TypeId;
    internal object Data;
    internal Vector2 Position;
    internal float Rotation;
}

internal sealed class CustomPropMarker : MonoBehaviour
{
    internal CustomPropInstance Instance;
    internal ICustomPropDefinition Definition;
}
