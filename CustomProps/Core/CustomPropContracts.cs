using UnityEngine;

internal enum CustomPropFieldType
{
    Text,
    Integer,
    Float
}

internal enum CustomPropCategory
{
    Basic,
    Obstacle,
    Enemy,
    Decor,
    Misc,
    Trigger
}

internal sealed class CustomPropField
{
    internal string Label;
    internal string Placeholder;
    internal CustomPropFieldType Type;
    internal Func<object, string> Read;
    internal Action<object, string> Write;
    internal string SecondaryPlaceholder;
    internal CustomPropFieldType SecondaryType;
    internal Func<object, string> ReadSecondary;
    internal Action<object, string> WriteSecondary;

    internal CustomPropField(
        string label,
        string placeholder,
        CustomPropFieldType type,
        Func<object, string> read,
        Action<object, string> write)
    {
        Label = label;
        Placeholder = placeholder;
        Type = type;
        Read = read;
        Write = write;
    }

    internal CustomPropField(
        string label,
        string placeholder,
        CustomPropFieldType type,
        Func<object, string> read,
        Action<object, string> write,
        string secondaryPlaceholder,
        CustomPropFieldType secondaryType,
        Func<object, string> readSecondary,
        Action<object, string> writeSecondary)
        : this(label, placeholder, type, read, write)
    {
        SecondaryPlaceholder = secondaryPlaceholder;
        SecondaryType = secondaryType;
        ReadSecondary = readSecondary;
        WriteSecondary = writeSecondary;
    }
}

internal interface ICustomPropDefinition
{
    string TypeId { get; }
    string DisplayName { get; }
    string Description { get; }
    CustomPropCategory EditorCategory { get; }
    Sprite Icon { get; }
    object CreateDefaultData();
    string SerializeData(object data);
    object DeserializeData(string json);
    CustomPropField[] Fields { get; }
    void CreateRuntime(GameObject gameObject, object data);
}

internal abstract class CustomPropDefinition<TData> : ICustomPropDefinition
    where TData : new()
{
    public abstract string TypeId { get; }
    public abstract string DisplayName { get; }
    public abstract string Description { get; }
    public virtual CustomPropCategory EditorCategory => CustomPropCategory.Misc;
    public abstract Sprite Icon { get; }
    public abstract CustomPropField[] Fields { get; }

    public virtual TData CreateDefault()
    {
        return new TData();
    }

    public abstract void CreateRuntime(GameObject gameObject, TData data);

    object ICustomPropDefinition.CreateDefaultData()
    {
        return CreateDefault();
    }

    string ICustomPropDefinition.SerializeData(object data)
    {
        return JsonUtility.ToJson((TData)data);
    }

    object ICustomPropDefinition.DeserializeData(string json)
    {
        if (string.IsNullOrEmpty(json)) return CreateDefault();
        try
        {
            var result = JsonUtility.FromJson<TData>(json);
            return result == null ? (object)CreateDefault() : result;
        }
        catch
        {
            return CreateDefault();
        }
    }

    void ICustomPropDefinition.CreateRuntime(GameObject gameObject, object data)
    {
        CreateRuntime(gameObject, (TData)data);
    }

    protected static CustomPropField Text(
        string label,
        string placeholder,
        Func<TData, string> getter,
        Action<TData, string> setter)
    {
        return new CustomPropField(
            label,
            placeholder,
            CustomPropFieldType.Text,
            value => getter((TData)value) ?? string.Empty,
            (value, text) => setter((TData)value, text ?? string.Empty));
    }

    protected static CustomPropField Integer(
        string label,
        string placeholder,
        Func<TData, int> getter,
        Action<TData, int> setter,
        int minimum)
    {
        return new CustomPropField(
            label,
            placeholder,
            CustomPropFieldType.Integer,
            value => getter((TData)value).ToString(),
            (value, text) =>
            {
                int parsed;
                if (int.TryParse(text, out parsed)) setter((TData)value, Mathf.Max(minimum, parsed));
            });
    }

    protected static CustomPropField Float(
        string label,
        string placeholder,
        Func<TData, float> getter,
        Action<TData, float> setter,
        float minimum)
    {
        return new CustomPropField(
            label,
            placeholder,
            CustomPropFieldType.Float,
            value => getter((TData)value).ToString("0.##"),
            (value, text) =>
            {
                float parsed;
                if (float.TryParse(text, out parsed)) setter((TData)value, Mathf.Max(minimum, parsed));
            });
    }

    protected static CustomPropField FloatIntegerPair(
        string label,
        string floatPlaceholder,
        Func<TData, float> floatGetter,
        Action<TData, float> floatSetter,
        float floatMinimum,
        string integerPlaceholder,
        Func<TData, int> integerGetter,
        Action<TData, int> integerSetter,
        int integerMinimum)
    {
        return new CustomPropField(
            label,
            floatPlaceholder,
            CustomPropFieldType.Float,
            value => floatGetter((TData)value).ToString("0.##"),
            (value, text) =>
            {
                float parsed;
                if (float.TryParse(text, out parsed)) floatSetter((TData)value, Mathf.Max(floatMinimum, parsed));
            },
            integerPlaceholder,
            CustomPropFieldType.Integer,
            value => integerGetter((TData)value).ToString(),
            (value, text) =>
            {
                int parsed;
                if (int.TryParse(text, out parsed)) integerSetter((TData)value, Mathf.Max(integerMinimum, parsed));
            });
    }
}