# Affine Types in C#

## Introduction

This project introduces affine types to C#, enhancing the language's type system to enforce single-use constraints on 
certain instances. Affine types are a powerful tool for ensuring resource management and correctness in software, 
particularly useful in critical systems where errors must be caught at compile-time rather than runtime.

## What are Affine Types?

Affine types, often confused with linear types, are types that enforce the constraint that an instance of the type can 
be used at most once. This is in contrast to regular types, which can be used multiple times. By enforcing single-use, 
affine types help prevent issues such as double-free errors, resource leaks, and other usage-related bugs.

**Key Characteristics:**

- **Single Use:** Instances of affine types can only be used once.
- **Compile-Time Enforcement:** The constraint is checked at compile-time, ensuring no runtime overhead.
- **Enhanced Safety:** Guarantees correct usage of resources, eliminating certain classes of bugs.

## Why Implement Affine Types?

### Near Correct-by-Construction (CbC)

The primary motivation for implementing affine types is to move towards a Correct-by-Construction (CbC) paradigm. In 
CbC, software is designed in such a way that it is correct from the moment it is written. Affine types help achieve this
by:

- **Preventing Silent Errors:** By enforcing correct usage of types at compile-time, we eliminate silent errors that 
  could go unnoticed until production.
- **Ensuring Correctness:** Developers can be confident that their code is free from certain classes of bugs, reducing 
  the need for extensive testing and debugging.
- **Enhanced Reliability:** Especially useful for systems that require high reliability and security, such as IronVelo's
  Identity Provider (IdP).

## Usage in IronVelo's IdP SDK

IronVelo's IdP SDK leverages affine types to ensure that the correct usage of resources is enforced at the type system 
level. This means that if your code compiles without errors, you can be confident that there are no usage-related bugs 
outside of network reachability issues.

### Token Rotation Protocol

In our IdP, we use a token rotation protocol where each use of a token invalidates the old token. The IdP will reject 
any subsequent use of an invalidated token. Affine types ensure that these bugs are caught at build time, not runtime, 
providing a robust safeguard against improper token usage.

**Benefits:**

- **Zero Silent Errors:** By catching errors at compile-time, the SDK ensures that no silent errors make it to 
  production.
- **Increased Developer Confidence:** Developers using the SDK can trust that their resource management is correct.
- **Robustness:** The system is more robust being less prone to runtime errors, leading to more reliable software.

## Examples

**Define an Affine Type `Once`:**
```csharp
using AffineTypes;

[Affine("Once may only be used once")]
public class Once
{
    public Once(int num)
    {
        _val = num;
    }
    private int _val;
    
    // Consume Once, returning an updated instance
    public static Once AddFive(Once once)
    {
        once._val += 5;
        return once;
    }
}
```

**Basic Violation of `Once`:**
```csharp
var once = new Once(5);
Once.AddFive(once); // consume our instance of once
Once.AddFive(once); // Error: ATU001: 'once' cannot be reused: Once may only be used once
```

**Violation of `Once` in a Loop:**
```csharp
var once = new Once(5);
for (var i = 0; i < 5; i++) 
{
    // Error: ATU002: Loop violated affine type `once`: Once may only be used once
    Once.AddFive(once); 
}
```

**Remedying the Loop Violation of `Once`:**
```csharp
var once = new Once(5);
for (var i = 0; i < 5; i++) 
{
    // No error as `once` is valid for reentry of the loop
    once = Once.AddFive(once); 
}
```

**Understanding Control Flow:**
```csharp
var once = new Once(5);
switch (new Random().Next()) 
{
    case 7:
        Once.AddFive(once);
        break;
    case 42:
        once = Once.AddFive(once);
        break;
}

once = Once.AddFive(once); // Error: ATU001: 'once' cannot be reused: Once may only be used once
// The case 7 consumed our instance of `once`
```

```csharp
var once = new Once(5);
switch (new Random().Next()) 
{
    case 7:
        Once.AddFive(once);
        return;
    case 42:
        once = Once.AddFive(once);
        break;
}
once = Once.AddFive(once); // No error as the case which consumed once returned early, therefore this use is valid
```

## Limitations

### Methods

Right now, unlike associated functions (aka static methods), methods on the affine type are all non-consuming. Here's an
example depicting this behavior:

**Introducing a Method for Adding a Number to `Once`:**
```csharp
[Affine(/* ... */)]
public class Once
{
    // ...
    public void Add(int num)
    {
        _val += num;
    }
}
```

**Using Our Method:**
```csharp
var once = new Once(5);
once.Add(5);
once.Add(5); // No error as method's are non-consuming
```

While this example isn't problematic, one can add interfaces to their affine type which violates the properties of 
affine types. In the future, we may add semantics inspired by the Rust programming language's notion of borrowing. 
Without this notion, affine types will be overly restrictive. For our usage, we don't need this consumption; we only
need the API of our SDK to consume the type to prevent errors at runtime.

### Aliasing

We've made the decision to make the aliasing of an affine type illegal. In the initial implementation of our affine 
types, we allowed aliasing but tracked the usage of said aliases considering them the same instance. We decided to 
remove this and make aliasing illegal as this added complexity. With something coming directly from type theory, we 
prioritized simplicity so that we could better reason about our implementation.

**Example of the Error Seen When an Affine Type is Aliased:**
```csharp
var once = new Once(5);
var onceAlias = once; // Error: ATU003: 'once' cannot be safely aliased: Once may only be used once
```

## Summary

Affine types in C# enhance resource management and correctness by enforcing single-use constraints at compile-time. This
approach aligns with the Correct-by-Construction paradigm, preventing silent errors and increasing software reliability.
While there are limitations, such as non-consuming methods and illegal aliasing, the benefits in critical systems like 
IronVelo's IdP SDK are significant.
