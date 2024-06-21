# `MustUse` Attribute

## Introduction

This project introduces the `MustUse` attribute to C#. `MustUse` can be applied to types and methods to encourage the 
usage of their return values. It helps prevent needless computation and ensures that the caller handles the returned 
value appropriately, especially in cases where the return type represents a result or an error that should be handled.

### Inspiration

This attribute is inspired by the `#[must_use]` attribute in Rust, which serves a similar purpose of ensuring that the 
result of a function or method is used and not ignored.

## Usage

To use the `MustUse` attribute, simply apply it to a type or method declaration, providing an optional message as a 
parameter. The message can be used to provide additional context or guidance to the caller.

**Applying to Types:**
```csharp
[MustUse("The result of this operation should be handled.")]
public class Result
{
    // Class members...
}
```

When the `MustUse` attribute is applied to a type, it indicates that the instances of that type should be used and not 
ignored. If the return value of a method or function returning this type is not used, a warning will be displayed.

**Applying to Methods:**
```csharp
[MustUse("`AddFive` is pure, so ignoring the result indicates your usage is a waste of computation")]
public int AddFive(int x)
{
    return x + 5;
}
```

When the `MustUse` attribute is applied to a method, it indicates that the return value of that method should be used 
and not ignored. If the caller does not use the returned value, a warning will be displayed.

## Benefits

Using the `MustUse` attribute provides several benefits:

1. It helps prevent needless computation by ensuring that the caller uses the returned value of a method or function.
2. It encourages proper handling of return types, especially when dealing with result types or error conditions.
3. It improves code quality and readability by making the intent explicit and providing guidance to the caller.