# ResultAble

`ResultAble` is a utility for the IronVelo IdP SDK, designed to handle responses from the IdP in a type-safe manner. 
This utility is specifically tailored for use with IronVelo's SDK, generating source code that directly references types
exposed in the SDK package.

### Why Does ResultAble Exist?

`ResultAble` was created to simplify interactions with the IdP, which leverages sum types extensively. Many responses 
from the IdP are serialized sum types, and modeling these responses for every request can be cumbersome and error-prone. 
This utility streamlines the process by converting the IdP's responses into IronVelo's `Result` type, making the 
interactions easier to reason about and reducing boilerplate code. Note that this implementation is domain-specific and 
does not provide the ability to create sum types in C#. For general sum type support, consider libraries like
[`OneOf`](https://github.com/mcintyre321/OneOf).

### Example

Consider a login flow where we need to verify the user's MFA method. The IdP's response will either indicate that the 
user must retry or provide a token representing the login state (in the ret field). Using `ResultAble`, we can handle 
this response as follows:

Define the response type with the `Result` attribute:

```csharp
using ResultAble;

[Result]
internal partial record OtpCheckRes(
    [property: Ok] string Token,
    [property: Error] bool Failure
);
```

Convert `OtpCheckRes` into a result using the generated `ToResult` method:

```csharp
return res
    .ToResult()
    // Handle success or failure in a type-safe manner
    .MapOr(
        Result<Token, RetryInitMfa>.Failure(new RetryInitMfa(Client, res.Permit, MfaKinds)), 
        rawToken => Result<Token, RetryInitMfa>.Success(new Token(rawToken))
    );
```

By using `ResultAble`, we eliminate the need to check the nullability of options for every request, allowing us to focus
on ensuring our logic is correct. This leads to safer and more robust interactions with the IdP.