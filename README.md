# Experimental Type System

## Overview

This solution experiments with **algebraic sum types (union types)** in C# using an **incremental source generator**.

When a partial class is decorated with `[ExpermientalTyping]`, the generator scans expression-bodied properties of type `UnionType` and generates strongly-typed union wrappers.

### Key Features

- ✅ **N-ary unions** — supports unlimited types: `typeof(A) | typeof(B) | typeof(C) | ...`
- ✅ **Generic type support** — works with `List<T>`, `Dictionary<K,V>`, etc.
- ✅ **Implicit conversions** — assign any union member directly to the wrapper
- ✅ **Common property forwarding** — shared properties across all types are surfaced on the wrapper
- ✅ **Type-safe slots** — one nullable property per union member type
- ✅ **GetValue()** — retrieve the underlying value as `object`
- ✅ **[OneOf] attribute** — `GetValue()` is decorated with `[OneOf(typeof(T1), typeof(T2), ...)]` for runtime type info
- ✅ **Exhaustiveness analyzer** — errors when `switch` on `GetValue()` doesn't handle all types (ONEOF001)
- ✅ **Code fix provider** — IDE suggests "Add missing cases" to auto-generate missing switch arms

## Quick Start

```csharp
[ExpermientalTyping]
public partial class Consumer
{
    // Binary union
    public UnionType CurrentUser => typeof(User) | typeof(Profile);
    
    // N-ary union (4 types)
    public UnionType XUser => typeof(User) | typeof(Profile) | typeof(Profile2) | typeof(Profile3);
    
    // Generic types
    public UnionType LUser => typeof(List<User>) | typeof(List<Profile>) | typeof(List<Profile2>) | typeof(List<Profile3>);
}
```

### Usage

```csharp
// Implicit conversion from any union member
CurrentUserType current = new User("First", "Last", "Email");

// Access the underlying value
current.User.ShouldBeEquivalentTo(new User("First", "Last", "Email"));
current.Profile.ShouldBeNull();

// Access common properties directly on the wrapper
current.FirstName.ShouldBe("First");
current.LastName.ShouldBe("Last");

// Get the underlying value as object
object value = current.GetValue(); // returns the User instance

// N-ary union works the same way
XUserType xuser = new User("First", "Last", "Email");
xuser.FirstName.ShouldBe("First");
xuser.GetValue().ShouldBeOfType<User>();
```

## Solution Structure

```text
ExperimnetalTypeSystem.sln
├── ExperimnetalTypeSystem.Test/
│   ├── Consumer.cs              # Test class with union definitions
│   ├── ExpermientalTypingAttribute.cs
│   ├── UnionType.cs             # Marker type for union syntax
│   ├── User.cs
│   ├── Profile.cs               # Contains Profile, Profile2, Profile3
│   └── ExperimnetalTypeSystem.Test.csproj
└── ExperimnetalTypeSystem.Generator/
    ├── ExperimentalTypingGenerator.cs
    └── ExperimnetalTypeSystem.Generator.csproj
```

## How the Generator Works

The incremental generator in `ExperimentalTypingGenerator.cs`:

1. **Finds** partial classes decorated with `[ExpermientalTyping]`
2. **Scans** expression-bodied properties of type `UnionType`
3. **Flattens** chained `|` expressions to collect all `typeof(...)` operands
4. **Deduplicates** repeated types while preserving order
5. **Computes** common readable properties shared across all union types
6. **Generates** a nested wrapper class with:
   - One nullable slot per union type
   - Implicit conversion operator per union type
   - Forwarding getters for common properties

## Generated Type Shape

Given this source:

```csharp
[ExpermientalTyping]
public partial class Consumer
{
    public UnionType XUser => typeof(User) | typeof(Profile) | typeof(Profile2) | typeof(Profile3);
}
```

The generator produces:

```csharp
public partial class Consumer
{
    public XUserType XUserTypeValue => new();

    public partial class XUserType
    {
        public User? User { get; }
        public Profile? Profile { get; }
        public Profile2? Profile2 { get; }
        public Profile3? Profile3 { get; }

        public XUserType() { /* all slots default */ }
        
        private XUserType(User? value, Profile? value2, Profile2? value3, Profile3? value4) { /* assign slots */ }

        public static implicit operator XUserType(User value) => new(value, default, default, default);
        public static implicit operator XUserType(Profile value) => new(default, value, default, default);
        public static implicit operator XUserType(Profile2 value) => new(default, default, value, default);
        public static implicit operator XUserType(Profile3 value) => new(default, default, default, value);

        [OneOf(typeof(User), typeof(Profile), typeof(Profile2), typeof(Profile3))]
        public object GetValue()
        {
            if (User is not null) return User;
            if (Profile is not null) return Profile;
            if (Profile2 is not null) return Profile2;
            if (Profile3 is not null) return Profile3;
            throw new InvalidOperationException("XUserType has no value.");
        }

        // Common property: FirstName exists on all 4 types with same type (string)
        public string FirstName
        {
            get
            {
                if (User is not null) return User.FirstName;
                if (Profile is not null) return Profile.FirstName;
                if (Profile2 is not null) return Profile2.FirstName;
                if (Profile3 is not null) return Profile3.FirstName;
                throw new InvalidOperationException("XUserType has no value.");
            }
        }
    }
}
```

### Common Property Detection

A property is included in the generated wrapper only when **all** union types expose:

- Same property name
- Same property type
- Public getter
- Instance property (not static)

## Exhaustiveness Analyzer

The `OneOfExhaustivenessAnalyzer` checks that `switch` expressions and statements on `GetValue()` handle all possible types declared in the `[OneOf]` attribute.

### Diagnostic: ONEOF001

**"Switch is not exhaustive. Missing type(s): {types}"**

#### Example - Will trigger warning:

```csharp
CurrentUserType current = new User("First", "Last", "Email");

// ⚠️ ONEOF001: Switch is not exhaustive. Missing type(s): Profile
var result = current.GetValue() switch
{
    User u => $"User: {u.FirstName}",
    _ => "Unknown"  // discard doesn't count as handling Profile explicitly
};
```

#### Example - Exhaustive (no warning):

```csharp
CurrentUserType current = new User("First", "Last", "Email");

// ✅ All types handled explicitly
var result = current.GetValue() switch
{
    User u => $"User: {u.FirstName}",
    Profile p => $"Profile: {p.FirstName}",
    _ => throw new InvalidOperationException()  // discard is fine after all types handled
};
```

#### Switch statement support:

```csharp
XUserType xuser = new User("First", "Last", "Email");

// ✅ All 4 types handled
switch (xuser.GetValue())
{
    case User u:
        Console.WriteLine($"User: {u.FirstName}");
        break;
    case Profile p:
        Console.WriteLine($"Profile: {p.FirstName}");
        break;
    case Profile2 p2:
        Console.WriteLine($"Profile2: {p2.FirstName}");
        break;
    case Profile3 p3:
        Console.WriteLine($"Profile3: {p3.FirstName}");
        break;
    default:
        throw new InvalidOperationException();
}
```

### Supported patterns

The analyzer recognizes these pattern forms:
- `User u => ...` (declaration pattern)
- `User => ...` (type pattern)
- `User { } => ...` (recursive pattern with type)
- `case User u:` (case pattern in switch statement)

### Code Fix: "Add missing cases"

When the analyzer reports ONEOF001, the IDE will offer a quick fix to automatically add the missing cases:

**Before (with error):**
```csharp
var result = current.GetValue() switch
{
    User u => $"User: {u.FirstName}",
    _ => "Unknown"
};
// ❌ ONEOF001: Missing type(s): Profile
```

**After applying "Add missing cases":**
```csharp
var result = current.GetValue() switch
{
    User u => $"User: {u.FirstName}",
    Profile profile => throw new NotImplementedException(),
    _ => "Unknown"
};
// ✅ All types handled
```

The code fix:
- Inserts missing cases before the discard (`_`) or default case
- Generates `throw new NotImplementedException()` as placeholder
- Creates appropriate variable names from type names (e.g., `Profile` → `profile`, `List<User>` → `listUser`)

## Build and Test

### Build solution

```zsh
dotnet build
```

### Run tests

```zsh
dotnet test
```

### View generated files

Add to your `.csproj` to emit generated files to disk:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>$(BaseIntermediateOutputPath)/GeneratedFiles</CompilerGeneratedFilesOutputPath>
</PropertyGroup>
```

## Requirements

- .NET SDK with support for `net10.0`
- NuGet package restore enabled

## Current Limitations

- Only `typeof(A) | typeof(B) | ...` syntax is supported
- Only expression-bodied `UnionType` properties are detected
- No `Match` / `Switch` pattern-matching helpers (yet)
- Setter generation for shared properties not supported
- Exhaustiveness analyzer only works on direct `GetValue()` calls (not on variables assigned from it across methods)

## Algebraic Type Theory Context

This POC approximates **sum types** (discriminated unions) from algebraic type theory:

| Concept | This POC |
|---------|----------|
| Sum type `A + B + C` | `typeof(A) \| typeof(B) \| typeof(C)` |
| Case/variant | Nullable slot per type |
| Injection | Implicit conversion operator |
| Common interface | Auto-detected shared properties |

**Note:** This is a C# approximation, not a true ADT. The compiler does not enforce:
- Exactly one slot populated
- Exhaustive pattern matching

## Next Ideas

- Emit diagnostics for unsupported expressions
- Generate `Match<TResult>(Func<A, TResult>, Func<B, TResult>, ...)` methods
- Generate `Switch(Action<A>, Action<B>, ...)` methods
- Support interface-based common members
- Support method forwarding
