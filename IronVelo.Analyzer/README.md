# IronVelo.Analyzer

This directory contains custom analyzers designed to enhance the security and reliability of the IronVelo SDK, but can
enhance any project in C#. The two analyzers included are:

1. **Affine Types Analyzer**
2. **MustUse Attribute Analyzer**

These analyzers enforce best practices and coding standards, helping developers avoid common pitfalls and ensuring 
correct usage of critical constructs.

## Affine Types Analyzer

The **Affine Types Analyzer** introduces affine types to C#, enforcing single-use constraints on certain instances. 
Affine types ensure that an instance of a type can be used at most once, providing compile-time guarantees that prevent 
issues such as double-free errors and resource leaks.

### Key Features
- **Single Use Enforcement**: Ensures that instances of affine types are used only once.
- **Compile-Time Checks**: Provides compile-time guarantees, eliminating certain classes of runtime errors.
- **Enhanced Resource Management**: Helps prevent resource leaks and other usage-related bugs.

For a detailed explanation and usage examples, please refer to the [Affine Types README](AffineTypes.Analyzer/README.md).

## MustUse Attribute Analyzer

The **MustUse Attribute Analyzer** introduces the `MustUse` attribute to C#, encouraging the usage of return values from
methods and functions. This attribute helps prevent needless computation and ensures that important results are not 
ignored.

### Key Features
- **Usage Enforcement**: Ensures that the results of methods and functions annotated with `MustUse` are utilized.
- **Improved Code Quality**: Encourages proper handling of return values, reducing the likelihood of ignored results and
  associated bugs.
- **Enhanced Readability**: Makes the intent of methods and functions explicit, guiding developers towards correct 
  usage.

For more details on the `MustUse` attribute and its usage, please refer to the 
[MustUse Attribute README](MustUse.Analyzer/README.md).
