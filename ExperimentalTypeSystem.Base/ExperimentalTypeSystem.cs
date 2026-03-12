namespace ExperimentalTypeSystem.Base;

public static class ExperimentalTypeSystem
{
    extension(Type type) 
    {
        public static UnionType operator |(Type left, Type right) => new UnionType(left, right);
        public static UnionType operator |(UnionType left, Type right) => new UnionType([..left.Types,right]);

    }
}