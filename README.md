# Experimental Type System

## Checklist
- [x] Describe what the solution does
- [x] Explain how the source generator works
- [x] Document the generated union wrapper shape
- [x] Show how to run the xUnit tests
- [x] Note current assumptions and limitations

## Overview

This solution experiments with a small type-union style API in C# using an **incremental source generator**.

When a partial class is decorated with `[ExpermientalTyping]`, the generator scans expression-bodied properties of type `UnionType` that look like this:

```csharp
public UnionType CurrentUser => typeof(User) | typeof(Profile);
```

For each matching property, the generator creates a nested wrapper type whose name is based on the property name.

In the example above, it generates a nested type like:

- `CurrentUserType`

That generated type acts as a strongly typed two-way union wrapper for the left and right types.

## Solution Structure

```text
ExperimnetalTypeSystem.sln
├── ExperimnetalTypeSystem/
│   ├── Consumer.cs
│   ├── ExperimentalTypeSystem.cs
│   ├── ExpermientalTypingAttribute.cs
│   ├── ExperimnetalTypeSystem.csproj
│   ├── Profile.cs
│   ├── UnionType.cs
│   └── User.cs
└── ExperimnetalTypeSystem.Generator/
    ├── ExperimentalTypingGenerator.cs
    └── ExperimnetalTypeSystem.Generator.csproj
```

## How the Generator Works

The incremental generator in `ExperimnetalTypeSystem.Generator/ExperimentalTypingGenerator.cs`:

1. Finds partial classes decorated with `[ExpermientalTyping]`
2. Looks for expression-bodied properties of type `UnionType`
3. Detects binary expressions shaped like:
   - `typeof(LeftType) | typeof(RightType)`
4. Generates a nested wrapper class named from the property name:
   - `CurrentUser` → `CurrentUserType`
5. Adds implicit conversions from both union member types
6. Adds shared getter-only properties when both sides expose the same **public readable instance property** with the same type

## Generated Type Shape

Given this source:

```csharp
[ExpermientalTyping]
public partial class Consumer
{
    public UnionType CurrentUser => typeof(User) | typeof(Profile);
}
```

The generator produces a nested type conceptually like this:

```csharp
public partial class Consumer
{
    public CurrentUserType CurrentUserTypeValue => new();

    public partial class CurrentUserType
    {
        public User? User { get; }
        public Profile? Profile { get; }

        public static implicit operator CurrentUserType(User value) => new(value, default);
        public static implicit operator CurrentUserType(Profile value) => new(default, value);

        public string FirstName => User is not null ? User.FirstName : Profile!.FirstName;
        public string LastName  => User is not null ? User.LastName  : Profile!.LastName;
    }
}
```

### Important Details

- The generated wrapper stores one strongly typed slot per union member type.
- It does **not** depend on the containing instance.
- Only one slot is expected to be populated at a time.
- Shared properties are generated only when both sides expose:
  - the same property name
  - the same property type
  - a public getter
  - an instance property (not static)
- Shared properties are **getter-only**.

## Example Test

`ExperimnetalTypeSystem/Consumer.cs` contains an xUnit v3 test that exercises the generated wrapper:

```csharp
[Fact]
public void Test()
{
    CurrentUserType current = new User("First", "Last", "Email");
    current.User.ShouldBeEquivalentTo(new User("First", "Last", "Email"));
    current.Profile.ShouldBeNull();
    current.FirstName.ShouldBe("First");
    current.LastName.ShouldBe("Last");
}
```

## Build and Test

This project was converted **in place** into an xUnit v3 test project.

### Run tests

```zsh
dotnet test ExperimnetalTypeSystem/ExperimnetalTypeSystem.sln -v minimal
```

### Build solution

```zsh
dotnet build ExperimnetalTypeSystem/ExperimnetalTypeSystem.sln
```

## Requirements

- .NET SDK with support for `net10.0`
- Restore enabled for NuGet packages

## Current Limitations

This is intentionally experimental. The current generator supports:

- exactly two union operands
- only `typeof(A) | typeof(B)` syntax
- only expression-bodied `UnionType` properties
- nested generated wrapper classes
- implicit conversion from either side into the wrapper

Not currently handled:

- more than two types
- method forwarding
- field forwarding
- setter generation for shared properties
- advanced type-name normalization beyond simple identifier sanitization
- custom diagnostics for unsupported union expressions

## Troubleshooting

### Generated file still shows stale members

If you still see old generated members such as `Left` / `Right`, do a full rebuild:

```zsh
dotnet clean ExperimnetalTypeSystem/ExperimnetalTypeSystem.sln
dotnet build ExperimnetalTypeSystem/ExperimnetalTypeSystem.sln
```

### Record internals like `EqualityContract` appear in generated code

The generator now filters to public readable instance properties only. If stale output remains, rebuild the solution.

## Next Ideas

Possible follow-ups:

- emit diagnostics for unsupported expressions
- support more than two union members
- generate pattern-matching helpers
- generate `Match` / `Switch` methods
- improve generated member naming for generic and nested types

