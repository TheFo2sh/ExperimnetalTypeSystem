namespace ExperimentalTypeSystem.Base;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OneOfAttribute : Attribute
{
    public OneOfAttribute(params Type[] types)
    {
        Types = types;
    }

    public Type[] Types { get; }
}

