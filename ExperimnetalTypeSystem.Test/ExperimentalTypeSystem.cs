namespace ExperimnetalTypeSystem;

public static class ExperimentalTypeSystem
{
    extension(Type type) 
    {
        public static UnionType operator |(Type left, Type right) => new UnionType(left, right);
    }
}