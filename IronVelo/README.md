# C# SDK for IronVelo's IdP

## Introduction

The IronVelo C# SDK provides a secure and user-friendly interface for integrating with IronVelo's Identity Provider 
(IdP). This SDK empowers developers to focus on their core business logic while abstracting away the complexities of 
security and ensuring the robustness of their implementations.

### Key Features and Benefits

- **Secure by Design**: The SDK follows a Correct-by-Construction (CbC) paradigm, promoting secure coding practices and 
  reducing the likelihood of vulnerabilities.
- **Simplified Integration**: With a straightforward API and comprehensive documentation, the SDK streamlines the 
  integration process, saving developers time and effort.
- **Enhanced User Experience**: The SDK offers a seamless and intuitive user experience, with support for features like 
  multi-factor authentication (MFA) and secure token management.
- **Scalability and Performance**: Leveraging IronVelo's highly optimized IdP, the SDK enables applications to scale 
  effortlessly while maintaining optimal performance.
- **Extensibility**: The SDK provides a flexible architecture that allows developers to customize and extend its 
  functionality to suit their specific requirements.

### Correct-by-Construction (CbC) Approach

The IronVelo C# SDK embraces a Correct-by-Construction (CbC) approach, which means that security and correctness are 
built into the SDK from the ground up. By adhering to CbC principles, the SDK helps developers write code that is 
inherently secure and less prone to vulnerabilities.

The CbC approach offers several advantages:

- **Reduced Testing Burden**: With the SDK handling security aspects correctly by default, developers can focus on 
  testing their business logic rather than spending extensive efforts on security testing.
- **Increased Confidence**: The SDK's CbC design instills confidence in developers, knowing that their implementations 
  are built on a solid and secure foundation.
- **Prevention of Common Pitfalls**: The IdP's SDK architecture and API are designed to prevent common security pitfalls
  and guide developers towards secure coding practices.

### Getting Started

To start using the IronVelo C# SDK, follow these steps:

1. Install the SDK via [NuGet](https://www.nuget.org/packages/IronVelo/latest) or by manually downloading the package.
2. Explore the [Usage Examples](#sdk-usage-examples) section below and the `IronVelo.Example` project to learn how to 
   integrate the SDK into your application for common scenarios.

## SDK Usage Examples

This section provides real-life examples of how to use the IronVelo SDK for common scenarios like user signup, login, 
and account deletion. The examples demonstrate how to interact with the SDK using a type-safe approach and handle 
multistep flows securely.

### Basic Flow Examples

These examples are very far from how you would use this SDK in real life. We provide a real world example in the 
`IronVelo.Example` project, where we implemented a server which depicts how you can have multistep flows in a robust 
manner with a good UX and decent performance.

If you prefer serverless, you'll be happy to hear that the server tracks no state, meaning there's no need for sticky
connections, tracking sessions, etc. 

We wrote our examples in a way where you're safe copying and pasting them to get yourself started with integrations.

#### User Signup

The following example gives a basic outline of how sign up flows work in the SDK, for a realistic example see 
`IronVelo.Example/Controllers/SignupController`.

```csharp
using IronVelo;
using IronVelo.Types;
// ...

// Start the signup process with the desired username
Token token = await sdk.Signup()
    // request the username
    .Start(username).MapErr(err => err.ToString())
    // set the password
    .MapFut(s => s.Password(password))
    // select TOTP as the MFA kind (options: Totp, Sms, Email)
    .MapFut(s => s.Totp())
    // have the user guess, failure results in a retry state
    .BindFut(s => s.Guess(totpGuess).MapErr(retry => retry.Serialize()))
    // the user only wants TOTP, so they finish the process
    .MapFut(s => s.Finish())
    // If any of the states failed, throw
    .Unwrap();
```

#### User Login

The following example gives a basic outline of how log in flows work in the SDK, for a realistic example see 
`IronVelo.Example/Controllers/LoginController`.

```csharp
Token token = await sdk.Login()
    // Attempt to initiate the flow with username and password
    .Start(username, password).MapErr(err => err.ToString())
    // Select the MfaKind the user would like to use (available kinds are accessible from the state)
    .BindFut(s => s.Totp().MapErr(selectAgain => selectAgain.Serialize(/* user did not set TOTP up */)))
    // Verify the TOTP
    .BindFut(s => s.Guess(totp).MapErr(selectAgain => selectAgain.Serialize(/* wrong! */)))
    // If any of the states failed, throw
    .Unwrap();
```

#### Account Deletion

The following example gives a basic outline of how to schedule a user's account deletion in the SDK. A more realistic 
example is coming shortly, in the meantime the documentation will serve you well + all multistep flows operate in the
same manner, so referencing the existing examples can be valuable.

```csharp
var _ = await sdk.DeleteUser()
    // Request deletion, providing the user's token and asking them to confirm their username.
    .Delete(token, username).MapErr(err => err.Reason.ToString())
    // For added assurance that this is a legitimate request, confirm they know their password
    .BindFut(s => s.CheckPassword(password).MapErr(err => err.Reason.ToString()))
    // Finally, schedule the account's deletion
    .MapFut(s => s.Confirm())
    // If any of the states failed, throw
    .Unwrap();
```

#### Migrate Login (Non-MFA to MFA)

The following example gives a basic outline of how to migrate an existing user without MFA to our IdP. A more realistic
example is coming shortly, in the meantime, the existing examples are highly applicable as `MigrateLogin` is a blend 
between signup and login, in fact, you can reuse your logic from these flows to implement `MigrateLogin`.
It also is best to not lean entirely on the examples and read the documentation.

```csharp
Token token = await sdk.MigrateLogin()
    // Initiate the migrating login flow
    .Start(username, password)
    // If there was an error, it could have been due to WrongFlow, the LoginError will tell you this.
    .MapErr(err => err.ToString())
    // Now, the user is prompted to select an MFA kind they want to set up, select TOTP
    // This provides the provisioning Uri to use in an authenticator app
    .MapFut(s => s.Totp()) 
    // Confirm that TOTP was successfully setup by having the user verify the code, error means retry
    .BindFut(s => s.Guess(totp).MapErr(err => err.Serialize()))
    // Either setup another MFA kind or finish the login. The user must use the default flow going forward.
    .MapFut(s => s.Finish())
    // If any of the states failed, throw
    .Unwrap();
```

### Token Management Examples

The IronVelo SDK provides a simple and secure token management protocol. The following examples demonstrate how to check
the validity of a token and revoke all tokens for a user.

#### Checking Token Validity

It is important to check the validity of a token on practically all authenticated requests. The `CheckToken` method 
ensures that the token has not been revoked or stolen, enhancing security and performance compared to traditional 
identity providers. We've also put significant effort in ensuring this endpoint is incredibly fast, using this on 
every request will not come at the cost of response times.

```csharp
await sdk.CheckToken(token).MapOrElse(
    error => {/* handle the error ... */},
    peeked => {
        var userId = peeked.UserId;
        var newToken = peeked.Token;
        
        /* use the user's identifier ... */
    }
);
```

In this example, the CheckToken method is called with the current token. If the token is valid, a new `PeekedToken` is 
returned, containing the updated token that must be used for any further interactions. If the token is invalid, an error
is returned.

**Important Considerations:**

- The provided token becomes invalid after the `CheckToken` operation.
- The returned `PeekedToken` includes a new token that must be used for any further interactions.
- This method enhances security by preventing the reuse of tokens and ensuring that token revocations are respected,
  mitigating the risk of token theft and replay attacks.

#### Revoking All Tokens for a User

In certain scenarios, you may need to log out a user from all their sessions. The `RevokeTokens` method allows you to 
revoke all tokens associated with a user.

```csharp
var result = await sdk.RevokeTokens(token).MapOrElse(
    err => {
        // in incredibly rare cases, the IdP may not have been able to rotate the token. If this is the case, it means 
        // where we host our infrastructure is having outages.
        var token = err.Unwrap();
        
        // handle the error, perhaps retry the request with the new Token.
    },
    ok => {
        // All sessions were logged out, including this one, so no token is returned.
    }
);
```

In this example, the `RevokeTokens` method is called with the current token. If the operation is successful, all tokens 
associated with the user are revoked. If the operation fails and returns a new token, you should handle the case 
accordingly.

These token management examples demonstrate how the IronVelo SDK simplifies token handling while providing enhanced 
security features. By regularly checking token validity and properly handling token revocation, you can ensure the 
integrity and security of your application's authentication process.

## Ecosystem Contributions

### Constant-Time Base64

General implementations of Base64 typically leak information about the data being encoded/decoded via something 
called a side-channel (more specifically, leaving room for a timing attack). Ensuring constant-time properties is 
challenging, as you're working against the JIT. Modern compilers are smart, and often times they will optimize out 
theoretically constant-time implementations. 

**What are the Basic Rules to a Constant-Time Implementation?**

- **No Branching on Data:** Modern CPUs leverage something known as speculative execution to remedy the cost of 
  branching (which significantly disrupts a pipeline, very slow). With speculative execution, the CPU will guess the 
  outcome of a branch (such as an if statement) and execute the instructions associated with the guess. Guessing 
  wrong (a pipeline stall/bubble), which is especially common with random secret data such as keys, introduces a 
  measurable and large delay as the CPU tries to backtrack. Therefore, constant-time programming requires leveraging
  branch-free code.
- **No Lookup Tables:** With non-local memory access the CPU will generally cache the data. Implementations running on
  a CPU with a data cache will exhibit data-dependent timing variations. This is how the C# builtin Base64 
  implementation violates constant-time principles, leaking information about the data being encoded/decoded.
- **Avoid Compiler Optimizations:** In modern systems programming languages one can generally blackbox their 
  implementations preventing the compiler from optimizing out their constant-time algorithm. In higher-level languages
  such as C# where most optimizations are performed by the JIT this is significantly more nuanced. From our analysis 
  we found that the JIT is far less likely to violate these properties by keeping the implementation lower-level, 
  doing things such as operating directly on raw pointers.
- **Avoid Certain Mathematical Operations:** On most processors division is non-constant time, and in older processors 
  multiplication carries the same timing properties as division. Also, in CPUs without a barrel shifter, shifts and 
  rotations are carried out in a loop, so the amount to shift must not be a secret.

**How to Know if Constant-Time Properties were Achieved/Preserved?**

Verifying constant-time properties of an implementation is incredibly nuanced, as the constant-time properties one 
observed on one machine very well may not be preserved on others, this is especially challenging outside of systems 
programming languages (as there's more variables involved). 

The general approaches to auditing a constant-time implementation include:

- **Manual Review of the Assembly:** In general, the most practical way to analyze constant-properties is for an expert
  to analyze the assembly looking for non-constant-time instructions on the secret. Though, unfortunately this is
  not as reliable / possible in high-level languages such as C#.
- **Statistical Analysis/Black-Box Testing:** On top of manual review of the assembly, it is best to perform statistical
  analysis / black-box testing on one's constant time implementation. Unfortunately, manual review of the assembly is 
  not fool-proof, this is due to CPU manufacturers rarely publishing the inner-workings of their processor. Manual 
  review of the assembly is also not always possible like in high-level languages like C#. For our C# Base64 
  implementations we implemented the paper [`Dude, is my code constant time?`](https://eprint.iacr.org/2016/1123.pdf)
  to evaluate our constant-time properties.
- **Taint Analysis/Checking:** Taint analysis is a technique that involves tracking the flow of data through a program
  and identifying data that is dependent on untrusted sources or secret information. In the context of constant-time
  programming, taint analysis can be used to ensure that secret data does not influence the program's behavior in a
  way that could leak information through timing side channels. Taint analysis works by associating a "taint" or label 
  with each piece of data in the program, indicating whether it is derived from a secret or untrusted source. The 
  analysis then tracks how this tainted data propagates through the program, flagging any instances where the tainted 
  data is used in a way that could leak information.
- **Formal Verification:** Formal verification is a technique that uses mathematical methods to prove that a program
  satisfies certain properties, such as constant-time execution. Formal verification can provide a high level of
  assurance that a program is free of certain types of vulnerabilities, but it can be complex and time-consuming to
  apply. One notable tool for formal verification of constant-time properties is `ctverif`, which is written in Gallina 
  with the Coq proof assistant. The approach used by `ctverif` is described in detail in the paper 
  [`Verifying Constant-Time Implementations`](https://www.usenix.org/system/files/conference/usenixsecurity16/sec16_paper_almeida.pdf).

### Affine Types in C#

In addition to our work on constant-time implementations, we have also extended the C# language with support for affine 
types. Affine types provide a powerful static typing discipline that allows developers to express and enforce single-use
constraints on certain instances at compile-time.

Affine types are particularly useful for ensuring proper resource management and preventing usage-related bugs. By 
enforcing single-use constraints, affine types help catch errors such as double-free bugs and resource leaks at 
compile-time, promoting a Correct-by-Construction (CbC) approach to software development.

Key benefits of affine types in C# include:

- **Zero Runtime Overhead:** Affine types are a compile-time feature and do not introduce any runtime overhead.
- **Enhanced Safety:** Affine types guarantee correct usage of resources, eliminating certain classes of bugs.
- **Increased Developer Confidence:** Developers can be confident that their code is free from usage-related errors.

In IronVelo's IdP SDK, we leverage affine types to enforce correct usage of resources at the type system level. For 
example, in our token rotation protocol, each use of a token invalidates the old token. Affine types ensure that any 
subsequent use of an invalidated token is caught at compile-time, providing a robust safeguard against improper token 
usage.

For a comprehensive overview of affine types and their implementation in C#, please refer to the dedicated 
[Affine Types README](../IronVelo.Analyzer/AffineTypes.Analyzer/README.md). The README covers the following topics:

- Detailed explanation of affine types and their characteristics
- Motivation behind implementing affine types in C#
- Usage examples and code snippets demonstrating affine types in action
- Limitations and considerations, such as non-consuming methods and illegal aliasing
- Benefits of affine types in critical systems like IronVelo's IdP SDK

We highly recommend exploring the Affine Types README to gain a deeper understanding of this powerful static typing 
feature and how it can be leveraged to write safer and more reliable C# code without sacrificing performance.

### `MustUse` Attribute

In addition to constant-time Base64 and affine types, we have also introduced the `MustUse` attribute to C#. The 
`MustUse` attribute can be applied to types and methods to encourage the usage of their return values. It helps prevent 
needless computation and ensures that the caller handles the returned value appropriately, especially in cases where the
return type represents a result or an error that should be handled.

The `MustUse` attribute is inspired by the `#[must_use]` attribute in Rust, which serves a similar purpose of ensuring 
that the result of a function or method is used and not ignored.

**Benefits of using the `MustUse` attribute:**

1. Prevents needless computation by ensuring that the caller uses the returned value of a method or function.
2. Encourages proper handling of return types, especially when dealing with result types or error conditions.
3. Improves code quality and readability by making the intent explicit and providing guidance to the caller.

To use the `MustUse` attribute, simply apply it to a type or method declaration, providing an optional message as a 
parameter. The message can be used to provide additional context or guidance to the caller.

```csharp
[MustUse("The result of this operation should be handled.")]
public class Result
{
    // Class members...
}

[MustUse("`AddFive` is pure, so ignoring the result indicates your usage is a waste of computation")]
public int AddFive(int x)
{
    return x + 5;
}
```

For more details on the MustUse attribute and its usage, please refer to the dedicated 
[`MustUse` README](../IronVelo.Analyzer/MustUse.Analyzer/README.md).