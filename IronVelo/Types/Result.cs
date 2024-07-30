using System.Runtime.CompilerServices;
using IronVelo.Exceptions;
using IronVelo.Utils;
using MustUse;
namespace IronVelo.Types;

/// <summary>
/// Represents the result of some operation.
/// </summary>
/// <typeparam name="T">The type of the successful result.</typeparam>
/// <typeparam name="TE">The type of the error result, representing non-critical failures.</typeparam>
/// <remarks>
/// <para>
/// The <see cref="Result{T, TE}"/> type is a pseudo-monadic structure used to represent the outcome of operations that
/// can succeed or fail. It provides methods for mapping, binding, and handling results in a functional style, promoting
/// safe and clear error handling.
/// </para>
/// <para>
/// <b>How can I deal with this?</b>
/// <br/>
/// Our Result type offers a comprehensive API for error handling and using our SDK in an elegant manner. As most of
/// our SDK is asynchronous, our Result type was written around this.
/// <br/><br/>
/// <b>I don't want to deal with this, I want my exceptions.</b><br/>
/// While we don't like this ourselves, the nice part about handling results in this manner is that it offers choice.
/// If you rather functional patterns you can take advantage of our extensive API, if you rather the more ~idiomatic~
/// approach in C#, you can simply use <see cref="Unwrap"/> and <see cref="UnwrapErr"/> which extract the type
/// associated with that result, and throw an exception if that wasn't the correct state.
/// </para>
/// <para>
/// <b>Available Methods:</b><br/>
/// Synchronous API:
/// <list type="bullet">
/// <item>
///     <term><see cref="Map{TNew}"/>:</term>
///     <description>
///     Apply some function to the success value, with the ability to change the type, resulting in a new
///     <see cref="Result{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapErr{TENew}"/>:</term>
///     <description>
///     Apply some function to the error value, with the ability to change the type, resulting in a new
///     <see cref="Result{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="Bind{TNew}"/>:</term>
///     <description>
///     Apply some function which results in a new <see cref="Result{T,TE}"/> to the success value. With
///     <c>Bind</c> you can transform the success type, but the error type must be the same. This results
///     in a new <see cref="Result{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapOrElse{TNew}"/>:</term>
///     <description>
///     Apply a function to the error and success types, both functions resulting in the same type. This lifts the
///     return value out of the <see cref="Result{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapOr{TNew}"/>:</term>
///     <description>
///     Provide a default value for the error state and apply some function on the success state which results in the
///     same type as the default value. This lifts the return value out of the
///     <see cref="Result{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="Unwrap"/>:</term>
///     <description>
///     If the <see cref="Result{T,TE}"/> was a success extract the success value, otherwise throw the exception
///     <see cref="NotOk"/>. This is helpful if you don't want to learn how to deal with results and rather use
///     exceptions.
///     </description>
/// </item>
/// <item>
///     <term><see cref="UnwrapErr"/>:</term>
///     <description>
///     If the <see cref="Result{T,TE}"/> was an error extract the error value, otherwise throw the exception
///     <see cref="NotErr"/>. This is helpful if you don't want to learn how to deal with results and rather use
///     exceptions.
///     </description>
/// </item>
/// <item>
///     <term><see cref="Inspect"/>:</term>
///     <description>
///     Apply some side-effect (<see cref="Action{T}"/>) on the success state, such as logging.
///     </description>
/// </item>
/// <item>
///     <term><see cref="InspectErr"/>:</term>
///     <description>
///     Apply some side-effect (<see cref="Action{T}"/>) on the error state, such as logging.
///     </description>
/// </item>
/// </list>
/// Asynchronous API:
/// <list type="bullet">
/// <item>
///     <term><see cref="MapFut{TNew}"/>:</term>
///     <description>
///     Apply some asynchronous function to the success value, with the ability to change the type, resulting in a new
///     <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapErrFut{TENew}"/>:</term>
///     <description>
///     Apply some asynchronous function to the error value, with the ability to change the type, resulting in a new
///     <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="BindFut{TNew}"/>:</term>
///     <description>
///     Apply some function which results in a new <see cref="FutResult{T,TE}"/> to the success value. With
///     <c>BindFut</c> you can transform the success type, but the error type must be the same. This results
///     in a new <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>Why handle Results like this?</b>
/// <br/>
/// We handle Results in a pseudo-monadic structure to move towards achieving something known as referential
/// transparency. While we do throw exceptions in certain scenarios, we use this as an indicator of something being
/// seriously wrong. It also allows us to elegantly model state transitions in our flows, allowing the types to act
/// as a form of documentation.
/// <br/>
/// <para>
/// <b>Our Usage</b><br/>
/// Most of our flows can transition to one of two states, for example, with MFA verification it can either transition
/// to the state to finalize the flow or retry the OTP. By using a <see cref="FutResult{T,TE}"/> type we can describe
/// these possibilities in the type itself, and allow for simple handling of these possible transitions. 
/// </para>
/// <para>
/// <b>Referential Transparency</b><br/>
/// If function <c>f()</c> is given the same input, it should result in the same output. This means that the caller
/// should have clarity regarding the outcome without resorting to reading the source code. In cybersecurity, it is
/// critical that things are not misused, while none of the security properties our IdP offers can be violated via an
/// SDK, we don't want our users to experience bugs and or downtime due to misuse. Much of our contributions to the C#
/// ecosystem in creating our SDK was for helping write safer code and getting closer to achieving something called the
/// Correct-by-Construction paradigm. In this paradigm it means that if your code builds it most likely works.
/// <br/><br/>
/// We want integration to be easy, not a headache. Reducing the number of tests our customers have to write and taking
/// that responsibility onto ourselves is the goal.
/// <br/><br/>
/// We want your life to be easier, not harder, which is why we leverage approaches such as this.
/// </para>
/// </para>
/// </remarks>
[MustUse("You must handle the possible error (or use `.Unwrap()` to simply throw)")]
public class Result<T, TE>
{
    /// <summary>
    /// A representation of a successful transition
    /// </summary>
    private T? Value { get; }
    
    /// <summary>
    /// A representation of a non-critical transition failure
    /// </summary>
    private TE? Error { get; }

    /// <summary>
    /// Whether the result is an `Error` (false) or `Value` (true)
    /// </summary>
    private readonly bool _isSuccess;
    
    private Result(T? value, TE? error, bool isSuccess)
    {
        Value = value;
        Error = error;
        _isSuccess = isSuccess;
    }

    // state constructors
    
    /// <summary>
    /// Creates a successful result.
    /// </summary>
    /// <param name="value">The value of the successful result.</param>
    /// <returns>A Result object representing success.</returns>
    public static Result<T, TE> Success(T value) => new(value, default, true);
    
    /// <summary>
    /// Creates a failed result.
    /// </summary>
    /// <param name="error">The error value of the failed result.</param>
    /// <returns>A Result object representing failure.</returns>
    public static Result<T, TE> Failure(TE error) => new(default, error, false);

    // display / debugging
    
    /// <summary>
    /// Converts the result to a string for debugging purposes.
    /// </summary>
    /// <returns>A string representation of the result.</returns>
    public override string ToString() =>
        _isSuccess ? $"Success({Value})" : $"Failure({Error})";
    
    // combinatorics
    
    /// <summary>
    /// Maps the successful value to a new result using the provided function.
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="mapFunc">The function to map the value.</param>
    /// <returns>A new Result object with the mapped value or the original error.</returns>
    /// <example>
    /// Map can be used to transform the successful value in a Result object. Here's an example:
    /// <code>
    /// Result&lt;string, string&gt;GetGreeting(int hour)
    /// {
    ///     if (hour &lt; 12)
    ///         return Result&lt;string, string&gt;
    ///             .Success("Good morning!");
    ///     if (hour &lt; 18)
    ///         return Result&lt;string, string&gt;
    ///             .Success("Good afternoon!");
    ///     return Result&lt;string, string&gt;
    ///         .Success("Good evening!");
    /// }
    /// //
    /// string ToUpper(string greeting)
    ///     => greeting.ToUpper();
    /// //
    /// var result = GetGreeting(10) // Good morning!
    ///     .Map(ToUpper)            // GOOD MORNING!
    ///     .Expect("Failed to get greeting!");
    /// //
    /// // Outputs: GOOD MORNING!
    /// Console.WriteLine(result);  
    /// </code>
    /// </example>
    public Result<TNew, TE> Map<TNew>(Func<T, TNew> mapFunc) =>
        _isSuccess ? Result<TNew, TE>.Success(mapFunc(Value!)) : Result<TNew, TE>.Failure(Error!);
    
    /// <summary>
    /// Maps the error value to a new result using the provided function.
    /// </summary>
    /// <typeparam name="TENew">The type of the new error value.</typeparam>
    /// <param name="mapFunc">The function to map the error.</param>
    /// <returns>A new Result object with the original value or the mapped error.</returns>
    /// <example>
    /// MapErr can be used to transform the error value in a Result object. Here's an example:
    /// <code>
    /// Result&lt;int, int&gt; SafeDivide(int num, int den)
    /// {
    ///     return den == 0
    ///         ? Result&lt;int, string&gt;.Failure(0)
    ///         : Result&lt;int, string&gt;.Success(num / den)
    /// }
    /// //
    /// var result = SafeDivide(10, 0).MapErr(x => x + 42);
    /// //
    /// // Outputs: Failure(42)
    /// Console.WriteLine(result);  
    /// </code>
    /// </example>
    public Result<T, TENew> MapErr<TENew>(Func<TE, TENew> mapFunc) =>
        _isSuccess ? Result<T, TENew>.Success(Value!) : Result<T, TENew>.Failure(mapFunc(Error!));
    
    /// <summary>
    /// Binds the successful value to a new result using the provided function.
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="bindFunc">The function to bind the value.</param>
    /// <returns>A new Result object from the binding function or the original error.</returns>
    /// <example>
    /// Bind can help you write safer code, here's an example of this:
    /// <code>
    /// Result&lt;int, string&gt; ParseInt(string input)
    /// {
    ///     if (int.TryParse(input, out int value))
    ///         return Result&lt;int, string&gt;.Success(value);
    ///     return Result&lt;int, string&gt;.Failure("Invalid integer");
    /// }
    /// Result&lt;int, string&gt; SafeDivide(int num, int den)
    /// {
    ///     return den == 0
    ///         ? Result&lt;int, string&gt;.Failure("Division by zero")
    ///         : Result&lt;int, string&gt;.Success(num / den);
    /// }
    /// 
    /// Result&lt;int, string&gt; DivideByTwo(int num)
    ///     => SafeDivide(num, 2);
    /// <br/>
    /// Result&lt;uint, string&gt; SafeCast(int number)
    /// {
    ///     return number >= 0
    ///         ? Result&lt;uint, string&gt;.Success((uint)number)
    ///         : Result&lt;uint, string&gt;.Failure("Requires sign");
    /// }
    /// 
    /// uint result = ParseInt("10")
    ///     .Bind(DivideByTwo)
    ///     .Bind(SafeCast);
    ///
    /// // Output: Success(10)
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public Result<TNew, TE> Bind<TNew>(Func<T, Result<TNew, TE>> bindFunc) =>
        _isSuccess ? bindFunc(Value!) : Result<TNew, TE>.Failure(Error!);
    
    /// <summary>
    /// Maps the successful value or returns a default value if the result is an error.
    /// </summary>
    /// <typeparam name="TNew">The type of the new value.</typeparam>
    /// <param name="d">The default value function to apply if the result is an error.</param>
    /// <param name="map">The function to map the successful value.</param>
    /// <returns>The mapped value or the default value.</returns>
    /// <example>
    /// MapOrElse can be used to provide a fallback value when an operation fails. Here's an example:
    /// <code>
    /// Result&lt;int, string&gt; ParseInt(string input)
    /// {
    ///     if (int.TryParse(input, out int val))
    ///         return Result&lt;int, string&gt;
    ///             .Success(val);
    ///     return Result&lt;int, string&gt;
    ///         .Failure("Invalid integer");
    /// }
    /// 
    /// var result = ParseInt("NAN")
    ///     .MapOrElse(
    ///         error => -1,
    ///         val => val * 2
    ///     );
    /// 
    /// // Outputs: -1
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public TNew MapOrElse<TNew>(Func<TE, TNew> d, Func<T, TNew> map) => _isSuccess ? map(Value!) : d(Error!);
    
    /// <summary>
    /// Maps the successful value or returns a default value if the result is an error.
    /// </summary>
    /// <typeparam name="TNew">The type of the new value.</typeparam>
    /// <param name="d">The default value.</param>
    /// <param name="map">The function to map the successful value.</param>
    /// <returns>The mapped value or the default value.</returns>
    /// <example>
    /// MapOr provides a simpler way to handle errors by using a default value. Here's an example:
    /// <code>
    /// Result&lt;int, string&gt; ParseInt(string input)
    /// {
    ///     if (int.TryParse(input, out int val))
    ///         return Result&lt;int, string&gt;
    ///             .Success(val);
    ///     return Result&lt;int, string&gt;
    ///             .Failure("Invalid integer");
    /// }
    /// 
    /// var result = ParseInt("NAN")
    ///     .MapOr(-1, val => val * 2);
    /// 
    /// // Outputs: -1
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public TNew MapOr<TNew>(TNew d, Func<T, TNew> map) => _isSuccess ? map(Value!) : d;
    
    /// <summary>
    /// Apply some asynchronous function on the success variant of the result
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="mapFunc">The asynchronous function to map the value.</param>
    /// <returns>
    /// A future result (task) with further combinatorics allowing you to defer resolving the task until later
    /// </returns>
    /// <example>
    /// MapFut works similar to <see cref="Map{TNew}"/>, but allows you to use an asynchronous function to transform the
    /// result in a clean manner.
    /// <code>
    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100); // Simulate async work
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(42)
    ///     .MapFut(AsyncFetchData);
    ///
    /// // Outputs: Success(84)
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public FutResult<TNew, TE> MapFut<TNew>(Func<T, Task<TNew>> mapFunc) =>
        FutResult<TNew, TE>.From(
            _isSuccess
                ? mapFunc(Value!).ContinueWith(res => Result<TNew, TE>.Success(Resolve.Get(res)))
                : Task.FromResult(Result<TNew, TE>.Failure(Error!))
        );
    
    /// <summary>
    /// Maps the error value to a new result using the provided asynchronous function.
    /// </summary>
    /// <typeparam name="TENew">The type of the new error value.</typeparam>
    /// <param name="mapFunc">The asynchronous function to map the error.</param>
    /// <returns>
    /// A future result (task) with further combinatorics allowing you to defer resolving the task until later
    /// </returns>
    /// <example>
    /// MapErrFut can be used to transform the error value in an asynchronous 
    /// Result object. Here's an example:
    /// <code>
    /// Result&lt;int, int&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, int&gt;.Success(id)
    ///     : Result&lt;int, int&gt;.Failure(id);
    ///
    /// async Task&lt;string&gt; Ban(int id)
    /// {
    ///     await Task.Delay(100); // Ban them!
    ///     return $"Banned user: {id}"
    /// }
    ///
    /// var result = await CheckId(84)
    ///     .MapErrFut(Ban);
    ///
    /// // Output: Failure("Banned user: 84)
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public FutResult<T, TENew> MapErrFut<TENew>(Func<TE, Task<TENew>> mapFunc) =>
        FutResult<T, TENew>.From(
            _isSuccess
                ? Task.FromResult(Result<T, TENew>.Success(Value!))
                : mapFunc(Error!).ContinueWith(res => Result<T, TENew>.Failure(Resolve.Get(res)))
        );

    /// <summary>
    /// Binds the successful value to a new result using the provided asynchronous function.
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="bindFunc">The function returning the <see cref="FutResult{T,TE}"/>.</param>
    /// <returns>
    /// A future result (task) with further combinatorics allowing you to defer resolving the task until later
    /// </returns>
    /// <example>
    /// BindFut can help you write safer code, here's an example of this:
    /// <code>
    /// Result&lt;int, string&gt; NeedsBan(int id) => id &lt; 42
    ///     ? Result&lt;int,string&gt;.Success(id)
    ///     : Result&lt;int,string&gt;.Failure("No");
    /// 
    /// async Task&lt;Result&lt;bool, string&gt;&gt; Ban(int id)
    /// {
    ///     // our ban fails due to the network
    ///     return Result&lt;bool, string&gt;
    ///         .Failure($"Failed to ban {id}");
    /// }
    ///
    /// var result = await NeedsBan(7)
    ///     .BindFut(r => FutResult&lt;bool, string&gt;
    ///         .From(Ban(r));
    ///
    /// // Output: Failure("Failed to ban 7")
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    /// <remarks>
    /// Why <see cref="FutResult{T,TE}"/> rather than <c>Task&lt;Result&lt;T, TE&gt;&gt;</c>?
    /// <br/>
    /// This result type is designed specifically for making our API safer, offering close to referential transparency
    /// (for common failures which are anticipated), so BindFut is meant to be compatible with our API rather than
    /// universal compatibility with the ecosystem. <see cref="FutResult{T,TE}"/> can also be awaited itself, plus
    /// you can easily just reference the <c>Task</c> property of <see cref="FutResult{T,TE}"/>.
    /// </remarks>
    public FutResult<TNew, TE> BindFut<TNew>(Func<T, FutResult<TNew, TE>> bindFunc) =>
        _isSuccess
            ? bindFunc(Value!)
            : FutResult<TNew, TE>.FromReady(Result<TNew, TE>.Failure(Error!));

    /// <summary>
    /// Inspects the successful value using the provided action.
    /// </summary>
    /// <param name="action">The action which inspects the value.</param>
    /// <returns>The original <see cref="Result{T,TE}"/> object.</returns>
    /// <example>
    /// Inspect can be used to perform side effects on the successful value without changing it. For example:
    /// <code>
    /// var result = Result&lt;int, string&gt;
    ///     .Success(42);
    /// result.Inspect(
    ///     v => Console.WriteLine(v)
    /// );
    /// </code>
    /// </example> 
    public Result<T, TE> Inspect(Action<T> action)
    {
        if (_isSuccess) action(Value!);
        return this;
    }
    
    /// <summary>
    /// Inspects the error value using the provided action.
    /// </summary>
    /// <param name="action">The action to inspect the error.</param>
    /// <returns>The original <see cref="Result{T,TE}"/> object.</returns>
    /// <example>
    /// InspectErr can be used to perform side effects on the error value without changing it. For example:
    /// <code>
    /// var result = Result&lt;int, string&gt;
    ///     .Failure("Bad!");
    /// result.InspectErr(
    ///     err => Console.WriteLine(err)
    /// );
    /// </code>
    /// </example>
    public Result<T, TE> InspectErr(Action<TE> action)
    {
        if (!_isSuccess) action(Error!);
        return this;
    }

    /// <summary>
    /// Returns the successful value if the result is a success, otherwise throws a NotOk exception.
    /// </summary>
    /// <returns>The successful value.</returns>
    /// <exception cref="NotOk">if the result is an error.</exception>
    /// <example>
    /// Unwrapping a value is not the recommended way of handling an error, but if you prefer exceptions this gives an
    /// easy escape hatch from pseudo-monadic result handling. For example:
    /// <code>
    /// var res = Result&lt;int, string&gt;
    ///     .Success(42);
    /// int val = res.Unwrap();
    /// // Outputs: 42
    /// Console.WriteLine(val);
    /// </code>
    /// </example>
    public T Unwrap() => _isSuccess ? Value! : throw new NotOk($"Unwrapped on an Err value: {Error}");
    
    /// <summary>
    /// Returns the error value if the result is an error, otherwise throws a NotErr exception.
    /// </summary>
    /// <returns>The error value.</returns>
    /// <exception cref="NotErr">if the result is a success.</exception>
    /// <example>
    /// <code>
    /// var res = Result&lt;int, string&gt;
    ///     .Failure("Error!");
    /// int val = res.UnwrapErr();
    /// // Outputs: "Error!"
    /// Console.WriteLine(val);
    /// </code>
    /// </example>
    public TE UnwrapErr() => _isSuccess ? throw new NotErr($"Unwrapped on an Ok value: {Value}") : Error!;

    /// <summary>
    /// Returns the successful value if the result is a success, otherwise throws a NotOk exception with the provided
    /// message.
    /// </summary>
    /// <param name="msg">The message to include in the exception</param>
    /// <returns>The successful value.</returns>
    /// <exception cref="NotOk">If the result is an error.</exception>
    /// <example>
    /// <code>
    /// var res = Result&lt;int, string&gt;
    ///     .Success(42);
    /// int val = res // value is 42
    ///     .Expect("Failed!");
    /// </code>
    /// </example>
    public T Expect(string msg) => _isSuccess ? Value! : throw new NotOk($"Expect Failed: {msg}");
    
    /// <summary>
    /// Returns the successful value if the result is a success, otherwise throws a NotOk exception with a message
    /// generated by the provided function.
    /// </summary>
    /// <param name="op">
    /// The function which takes the error type and returns the message to include in the exception
    /// </param>
    /// <returns>The successful value.</returns>
    /// <exception cref="NotOk">If the result is an error.</exception>
    /// <example>
    /// <code>
    /// var res = Result&lt;int, string&gt;
    ///     .Success(42);
    /// int val = res // value is 42
    ///     .ExpectWith(
    ///         err => $"Failed! {err}"
    ///     );
    /// </code>
    /// </example>
    public T ExpectWith(Func<TE, string> op) => _isSuccess ? Value! : throw new NotOk($"Expect Failed: {op(Error!)}");
    
    /// <summary>
    /// Checks if the result is a success.
    /// </summary>
    /// <returns><c>true</c> if the result is a success, <c>false</c> otherwise.</returns>
    public bool IsOk() => _isSuccess;
    
    /// <summary>
    /// Checks if the result is an error.
    /// </summary>
    /// <returns><c>true</c> if the result is an error, <c>false</c> otherwise.</returns>
    public bool IsErr() => !_isSuccess;
    
    /// <summary>
    /// If the error and success type are the same, collapse the result into <c>T</c>
    /// </summary>
    /// <returns>The collapsed value.</returns>
    /// <exception cref="InvalidOperationException"><c>T != TE</c></exception>
    /// <example>
    /// In general it is better to take advantage of methods such as <see cref="MapOr{TNew}"/> or
    /// <see cref="MapOrElse{TNew}"/>, which have similar effect, but cannot result in runtime errors. An example
    /// of correct usage of <c>Collapse</c>:
    /// <code>
    /// // both Ok and Error states are the
    /// // same (requirement)
    /// var res = Result&lt;int, int&gt;
    ///     .Success(42);
    /// int val = res // value is 42
    ///     .Collapse();
    /// </code>
    /// </example>
    public T Collapse() 
    {
        return _isSuccess switch
        {
            true => Value!,
            false when Error is T error => error,
            _ => throw new InvalidOperationException("Cannot collapse a Result where T and TE are not the same type.")
        };
    }

    /// <summary>
    /// Ignore the values associated with the error, and transform it into a new <see cref="Result{T,TE}"/> based on
    /// the state.
    /// </summary>
    /// <param name="d">The value to associate with the <c>Error</c> state.</param>
    /// <param name="n">The value to associate with the <c>Success</c> state.</param>
    /// <typeparam name="TN">The new success type</typeparam>
    /// <typeparam name="TNe">The new error type</typeparam>
    /// <returns>
    /// The new <see cref="Result{T,TE}"/> with <c>d</c> as the error type and <c>n</c> as the success type
    /// </returns>
    public Result<TN, TNe> As<TN, TNe>(TNe d, TN n) =>
        _isSuccess ? Result<TN, TNe>.Success(n) : Result<TN, TNe>.Failure(d);
}

/// <summary>
/// Represents the future result of some operation.
/// </summary>
/// <typeparam name="T">The type of the successful result.</typeparam>
/// <typeparam name="TE">The type of the error result, representing non-critical failures.</typeparam>
/// <remarks>
/// <para>
/// The <see cref="FutResult{T,TE}"/> type is an abstraction over a <see cref="Result{T,TE}"/> wrapped in a
/// <see cref="InnerTask"/>; allowing for more convenience in dealing with tasks and their eventual results.
/// <see cref="FutResult{T,TE}"/> offers a very similar API to <see cref="Result{T,TE}"/> but without you having to
/// await each operation or lean on blocking which is highly inefficient. All the combinatorics offered by
/// <see cref="FutResult{T,TE}"/> are non-blocking, and simply apply some sync or async function to the future
/// result of the operation. Often times approaches such as this are looked down upon due to them being error-prone,
/// implementing your own <see cref="InnerTask"/> type means you lose the static analysis offered by C# and may forget to
/// execute it. Thankfully, we added the <c>MustUse</c> analyzer to C# which solves this problem, allowing us to provide
/// a more convenient API without sacrificing safety.
/// </para>
/// <para>
/// <b>How can I deal with this?</b>
/// <br/>
/// Our Result type offers a comprehensive API for error handling and using our SDK in an elegant manner. Our API is
/// primarily asynchronous, and <see cref="FutResult{T,TE}"/> makes this more enjoyable to deal with.
/// <br/><br/>
/// <b>I don't want to deal with this, I want my exceptions.</b><br/>
/// While we don't like this ourselves, the nice part about handling results in this manner is that it offers choice.
/// If you rather functional patterns you can take advantage of our extensive API, if you rather the more ~idiomatic~
/// approach in C#, you can simply use <see cref="Unwrap"/> and <see cref="UnwrapErr"/> which extract the Task which
/// resolves into the value associated with that result, and throw an exception if that wasn't the correct state.
/// </para>
/// <para>
/// <b>Available Methods:</b><br/>
/// All methods on
/// Synchronous API:
/// <list type="bullet">
/// <item>
///     <term><see cref="Map{TNew}"/>:</term>
///     <description>
///     Apply some function to the future success value, with the ability to change the type, resulting in a new
///     <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapErr{TENew}"/>:</term>
///     <description>
///     Apply some function to the future error value, with the ability to change the type, resulting in a new
///     <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="Bind{TNew}"/>:</term>
///     <description>
///     Apply some function which results in a new <see cref="Result{T,TE}"/> to the future success value. With
///     <c>Bind</c> you can transform the success type, but the error type must be the same. This results
///     in a new <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapOrElse{TNew}"/>:</term>
///     <description>
///     Apply a function to the future error and success types, both functions resulting in the same type. This lifts
///     the return value out of the <see cref="FutResult{T,TE}"/> as a <see cref="InnerTask"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapOr{TNew}"/>:</term>
///     <description>
///     Provide a default value for the future error state and apply some function on the future success state which
///     results in the same type as the default value. This lifts the return value out of the <see cref="Result{T,TE}"/>
///     as a <see cref="InnerTask"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="Unwrap"/>:</term>
///     <description>
///     If the <see cref="FutResult{T,TE}"/> was a success extract the success value as a <see cref="InnerTask"/>, otherwise
///     throw the exception <see cref="NotOk"/>. This is helpful if you don't want to learn how to deal with results and
///     rather use exceptions.
///     </description>
/// </item>
/// <item>
///     <term><see cref="UnwrapErr"/>:</term>
///     <description>
///     If the <see cref="FutResult{T,TE}"/> was an error extract the error value as a <see cref="InnerTask"/>, otherwise
///     throw the exception <see cref="NotErr"/>. This is helpful if you don't want to learn how to deal with results
///     and rather use exceptions.
///     </description>
/// </item>
/// <item>
///     <term><see cref="Inspect"/>:</term>
///     <description>
///     Apply some side-effect (<see cref="Action{T}"/>) on the future success state, such as logging.
///     </description>
/// </item>
/// <item>
///     <term><see cref="InspectErr"/>:</term>
///     <description>
///     Apply some side-effect (<see cref="Action{T}"/>) on the future error state, such as logging.
///     </description>
/// </item>
/// </list>
/// Asynchronous API:
/// <list type="bullet">
/// <item>
///     <term><see cref="MapFut{TNew}"/>:</term>
///     <description>
///     Apply some asynchronous function to the future success value, flattening the <see cref="InnerTask"/>s, with the
///     ability to change the type, resulting in a new <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="MapErrFut{TENew}"/>:</term>
///     <description>
///     Apply some asynchronous function to the future error value, flattening the <see cref="InnerTask"/>s, with the ability
///     to change the type, resulting in a new <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// <item>
///     <term><see cref="BindFut{TNew}"/>:</term>
///     <description>
///     Apply some function which results in a new <see cref="FutResult{T,TE}"/> to the future success value, flattening
///     the <see cref="InnerTask"/>s. With <c>BindFut</c> you can transform the success type, but the error type must be the
///     same. This results in a new <see cref="FutResult{T,TE}"/>.
///     </description>
/// </item>
/// </list>
/// </para>
/// <para>
/// <b>Why handle Results like this?</b>
/// <br/>
/// We handle Results in a pseudo-monadic structure to move towards achieving something known as referential
/// transparency. While we do throw exceptions in certain scenarios, we use this as an indicator of something being
/// seriously wrong. It also allows us to elegantly model state transitions in our flows, allowing the types to act
/// as a form of documentation.
/// <br/>
/// <para>
/// <b>Our Usage</b><br/>
/// Most of our flows can transition to one of two states, for example, with MFA verification it can either transition
/// to the state to finalize the flow or retry the OTP. By using a <see cref="Result{T,TE}"/> type we can describe
/// these possibilities in the type itself, and allow for simple handling of these possible transitions. 
/// </para>
/// <para>
/// <b>Referential Transparency</b><br/>
/// If function <c>f()</c> is given the same input, it should result in the same output. This means that the caller
/// should have clarity regarding the outcome without resorting to reading the source code. In cybersecurity, it is
/// critical that things are not misused, while none of the security properties our IdP offers can be violated via an
/// SDK, we don't want our users to experience bugs and or downtime due to misuse. Much of our contributions to the C#
/// ecosystem in creating our SDK was for helping write safer code and getting closer to achieving something called the
/// Correct-by-Construction paradigm. In this paradigm it means that if your code builds it most likely works.
/// <br/><br/>
/// We want integration to be easy, not a headache. Reducing the number of tests our customers have to write and taking
/// that responsibility onto ourselves is the goal.
/// <br/><br/>
/// We want your life to be easier, not harder, which is why we leverage approaches such as this.
/// </para>
/// </para>
/// </remarks>
[AsyncMethodBuilder(typeof(FutResultMethodBuilder<,>))]
[MustUse("A Task does nothing unless executed (with `await`, or if blocking is acceptable `.Result`)")]
public class FutResult<T, TE>
{
    internal FutResult(Task<Result<T, TE>> futRes)
    {
        InnerTask = futRes;
    }

    /// <summary>
    /// Create a new <see cref="FutResult{T,TE}"/> from a <see cref="Result{T,TE}"/> Task.
    /// </summary>
    /// <param name="futRes">The new <see cref="FutResult{T,TE}"/> instance.</param>
    /// <returns>The new <see cref="FutResult{T,TE}"/>.</returns>
    public static FutResult<T, TE> From(Task<Result<T, TE>> futRes) => new(futRes);
    
    /// <summary>
    /// Create a new <see cref="FutResult{T,TE}"/> from a <see cref="Result{T,TE}"/>
    /// </summary>
    /// <param name="res">The new <see cref="FutResult{T,TE}"/> instance.</param>
    /// <returns>The new, ready, <see cref="FutResult{T,TE}"/>.</returns>
    public static FutResult<T, TE> FromReady(Result<T, TE> res) => new(Task.FromResult(res));
    
    /// <summary>
    /// Convert a <c>Task&lt;FutResult&lt;T, TE&gt;&gt;</c> into a <see cref="FutResult{T,TE}"/>
    /// </summary>
    /// <param name="toFlatten">The nested <see cref="InnerTask"/> to flatten</param>
    /// <returns>The nested <see cref="InnerTask"/> flattened as a <see cref="FutResult{T,TE}"/></returns>
    public static FutResult<T, TE> Flatten(Task<FutResult<T, TE>> toFlatten) 
        => new(toFlatten.ContinueWith(task => Resolve.Get(task).InnerTask).Unwrap());

    /// <summary>
    /// The <see cref="InnerTask"/> associated with the <see cref="FutResult{T,TE}"/>
    /// </summary>
    public Task<Result<T, TE>> InnerTask { get; }

    /// <summary>
    /// Gets the result value of this <see cref="FutResult{T,TE}"/> via blocking.
    /// </summary>
    /// <returns>
    /// The underlying <see cref="Result{T, TE}"/> associated with the <see cref="FutResult{T,TE}"/>.
    /// </returns>
    /// <exception cref="AggregateException">
    /// The task was canceled. The <see cref="AggregateException.InnerExceptions"/> collection contains
    /// a <see cref="TaskCanceledException"/> object. -or- An exception was thrown during the execution of the task.
    /// The <see cref="AggregateException.InnerExceptions"/> collection contains information about the exception or
    /// exceptions.
    /// </exception>
    public Result<T, TE> Result => InnerTask.Result;
    
    /// <inheritdoc cref="Task.GetAwaiter"/>
    public TaskAwaiter<Result<T, TE>> GetAwaiter() => InnerTask.GetAwaiter();

    private Task<TR> Then<TR>(Func<Result<T, TE>, TR> app) 
        => InnerTask.ContinueWith(res => app(Resolve.Get(res)));
    
    private FutResult<TNew, TNe> ThenRes<TNew, TNe>(Func<Result<T, TE>, Result<TNew, TNe>> app) => new(Then(app));

    /// <summary>
    /// Maps the successful value to a new result using the provided function prior to the Task's resolution.
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="mapFunc">The function to map the value.</param>
    /// <returns>A new <see cref="FutResult{TNew,TE}"/> object with the mapped value or the original error.</returns>
    /// <example>
    /// Map can be used to transform the successful value in a Result object. Here's an example:
    /// <code>
    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100); // Simulate async work
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(42)
    ///     .MapFut(AsyncFetchData) // FutResult
    ///     .Map(Convert.ToString)
    ///     .Map(x => x.Length);
    ///
    /// // Outputs: Success(2)
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public FutResult<TNew, TE> Map<TNew>(Func<T, TNew> mapFunc) 
        => ThenRes(res => res.Map(mapFunc));
    
    /// <summary>
    /// Maps the error value to a new result using the provided function prior to the Task's resolution.
    /// </summary>
    /// <typeparam name="TENew">The type of the new error value.</typeparam>
    /// <param name="mapFunc">The function to map the error.</param>
    /// <returns>A new <see cref="FutResult{T,TENew}"/>object with the original value or the mapped error.</returns>
    /// <example>
    /// MapErr can be used to transform the error value in a Result object. Here's an example:
    /// <code>
    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100); // Simulate async work
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(7)
    ///     .MapFut(AsyncFetchData) // FutResult
    ///     .MapErr(err => $"No! {err}");
    ///
    /// // Output: Failure("No! Bad!")
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public FutResult<T, TENew> MapErr<TENew>(Func<TE, TENew> mapFunc)
        => ThenRes(res => res.MapErr(mapFunc));

    /// <summary>
    /// Binds the successful value to a new result using the provided function prior to the Task's resolution.
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="bind">The function to bind the value.</param>
    /// <returns>A new Result object from the binding function or the original error.</returns>
    /// <example>
    /// Bind can help you write safer code, here's an example of this:
    /// <code>
    /// Result&lt;int, string&gt; NeedsBan(int id) => id &lt; 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("No");
    /// 
    /// async Task&lt;Result&lt;int, string&gt;&gt; Ban(int id)
    /// {
    ///     return Result&lt;bool, string&gt;
    ///         .Success(id);
    /// }
    ///
    /// Result&lt;string, string&gt; LocalCleanup(int id)
    /// {
    ///     // cleanup anything stored locally associated
    ///     // with the banned user ...
    ///     return Result&lt;string, string&gt;
    ///         .Success($"Banned + Cleaned {id}");
    /// }
    ///
    /// var result = await NeedsBan(34)
    ///     .BindFut(r => FutResult&lt;int, string&gt;
    ///         .From(Ban(r))
    ///     .Bind(LocalCleanup);
    ///
    /// // Output: Success("Banned + Cleaned 34")
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public FutResult<TNew, TE> Bind<TNew>(Func<T, Result<TNew, TE>> bind) 
        => ThenRes(res => res.Bind(bind));

    /// <summary>
    /// Maps the successful value or returns a default value if the result is an error prior to the Task's resolution.
    /// </summary>
    /// <typeparam name="TNew">The type of the new value.</typeparam>
    /// <param name="d">The default value function to apply if the result is an error.</param>
    /// <param name="map">The function to map the successful value.</param>
    /// <returns>The mapped value or the default value.</returns>
    /// <example>
    /// MapOrElse can be used to provide a fallback value when an operation fails. Here's an example:
    /// <code>
    /// Result&lt;int, string&gt; ParseId(string id)
    /// {
    ///     if (int.TryParse(input, out int val))
    ///         return Result&lt;int, string&gt;
    ///             .Success(val);
    ///     return Result&lt;int, string&gt;
    ///         .Failure("Invalid ID");
    /// }
    ///
    /// async Task&lt;int&gt; SomeAsync(int id)
    /// {
    ///     return Result&lt;bool, string&gt;
    ///         .Success(id);
    /// }
    ///  
    /// var result = await ParseId("INVALID")
    ///     .MapFut(SomeAsync)
    ///     .MapOrElse(
    ///         error => -1,
    ///         val => val * 2
    ///     );
    /// 
    /// // Outputs: -1
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public Task<TNew> MapOrElse<TNew>(Func<TE, TNew> d, Func<T, TNew> map) 
        => Then(res => res.MapOrElse(d, map));

    /// <summary>
    /// Maps the successful value or returns a default value if the result is an error prior to the Task's resolution.
    /// </summary>
    /// <typeparam name="TNew">The type of the new value.</typeparam>
    /// <param name="d">The default value.</param>
    /// <param name="map">The function to map the successful value.</param>
    /// <returns>The mapped value or the default value.</returns>
    /// <example>
    /// MapOr provides a simpler way to handle errors by using a default value. Here's an example:
    /// <code>
    /// Result&lt;int, string&gt; ParseId(string id)
    /// {
    ///     if (int.TryParse(input, out int val))
    ///         return Result&lt;int, string&gt;
    ///             .Success(val);
    ///     return Result&lt;int, string&gt;
    ///         .Failure("Invalid ID");
    /// }
    ///
    /// async Task&lt;int&gt; SomeAsync(int id)
    /// {
    ///     return Result&lt;bool, string&gt;
    ///         .Success(id);
    /// }
    ///  
    /// var result = await ParseId("INVALID")
    ///     .MapFut(SomeAsync)
    ///     .MapOr(-1, val => val * 2);
    /// 
    /// // Outputs: -1
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public Task<TNew> MapOr<TNew>(TNew d, Func<T, TNew> map) 
        => Then(res => res.MapOr(d, map));
    
    /// <summary>
    /// Apply some asynchronous function on the success variant of the result, flattening the Tasks.
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="map">The asynchronous function to map the value.</param>
    /// <returns>
    /// A future result (task) with further combinatorics allowing you to defer resolving the task until later
    /// </returns>
    /// <example>
    /// MapFut works similar to <see cref="Map{TNew}"/>, but allows you to use an asynchronous function to transform the
    /// result in a clean manner.
    /// <code>
    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100); // Simulate async work
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(42)
    ///     .MapFut(AsyncFetchData)
    ///     .MapFut(AsyncFetchData) // again
    ///
    /// // Outputs: Success(168)
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public FutResult<TNew, TE> MapFut<TNew>(Func<T, Task<TNew>> map) 
        => FutResult<TNew, TE>.Flatten(Then(res => res.MapFut(map)));
    
    /// <summary>
    /// Maps the error value to a new result using the provided asynchronous function, flattening the Tasks.
    /// </summary>
    /// <typeparam name="TENew">The type of the new error value.</typeparam>
    /// <param name="mapErr">The asynchronous function to map the error.</param>
    /// <returns>
    /// A future result (task) with further combinatorics allowing you to defer resolving the task until later
    /// </returns>
    /// <example>
    /// MapErrFut can be used to transform the error value in an asynchronous 
    /// Result object. Here's an example:
    /// <code>
    /// Result&lt;int, int&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, int&gt;.Success(id)
    ///     : Result&lt;int, int&gt;.Failure(id);
    ///
    /// async Task&lt;int&gt; Ban(int id)
    /// {
    ///     await Task.Delay(100); // Ban them!
    ///     return id;
    /// }
    ///
    /// var result = await CheckId(84)
    ///     .MapErrFut(Ban)
    ///     // never can be too thorough
    ///     .MapErrFut(Ban);
    ///
    /// // Output: Failure(84)
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public FutResult<T, TENew> MapErrFut<TENew>(Func<TE, Task<TENew>> mapErr) 
        => FutResult<T, TENew>.Flatten(Then(res => res.MapErrFut(mapErr)));
    
    /// <summary>
    /// Binds the successful value to a new result using the provided asynchronous function, flattening the Tasks.
    /// </summary>
    /// <typeparam name="TNew">The type of the new successful value.</typeparam>
    /// <param name="bind">The function returning the <see cref="FutResult{T,TE}"/>.</param>
    /// <returns>
    /// A future result (task) with further combinatorics allowing you to defer resolving the task until later
    /// </returns>
    /// <example>
    /// BindFut can help you write safer code, here's an example of this:
    /// <code>
    /// Result&lt;int, string&gt; NeedsBan(int id) => id &lt; 42
    ///     ? Result&lt;int,string&gt;.Success(id)
    ///     : Result&lt;int,string&gt;.Failure("No");
    /// 
    /// async Task&lt;Result&lt;int, string&gt;&gt; Ban(int id)
    /// {
    ///     // our ban fails due to the network
    ///     return Result&lt;bool, string&gt;
    ///         .Failure($"Failed to ban {id}");
    /// }
    ///
    /// var result = await NeedsBan(7)
    ///     .BindFut(r => FutResult&lt;bool, string&gt;
    ///         .From(Ban(r))
    ///     // it's an APT so let's double ban them
    ///     .BindFut(r => FutResult&lt;bool, string&gt;
    ///         .From(Ban(r));
    /// 
    /// // Output: Failure("Failed to ban 7")
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    /// <remarks>
    /// Why <see cref="FutResult{T,TE}"/> rather than <c>Task&lt;Result&lt;T, TE&gt;&gt;</c>?
    /// <br/>
    /// This result type is designed specifically for making our API safer, offering close to referential transparency
    /// (for common failures which are anticipated), so BindFut is meant to be compatible with our API rather than
    /// universal compatibility with the ecosystem. <see cref="FutResult{T,TE}"/> can also be awaited itself, plus
    /// you can easily just reference the <c>Task</c> property of <see cref="FutResult{T,TE}"/>.
    /// </remarks> 
    public FutResult<TNew, TE> BindFut<TNew>(Func<T, FutResult<TNew, TE>> bind) 
        => FutResult<TNew, TE>.Flatten(Then(res => res.BindFut(bind)));
    
    /// <summary>
    /// Returns the successful value if the result is a success, otherwise throws a NotOk exception.
    /// </summary>
    /// <returns>A task which resolves into the successful value.</returns>
    /// <exception cref="NotOk">if the result is an error.</exception>
    /// <example>
    /// Unwrapping a value is not the recommended way of handling an error, but if you prefer exceptions this gives an
    /// easy escape hatch from pseudo-monadic result handling. For example:
    /// <code>
    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100); // Simulate async work
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(42)
    ///     .MapFut(AsyncFetchData)
    ///     .Unwrap();
    ///
    /// // Outputs: 168
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public Task<T> Unwrap() => Then(res => res.Unwrap());
    
    /// <summary>
    /// Returns the error value if the result is an error, otherwise throws a NotErr exception.
    /// </summary>
    /// <returns>A Task which resolves into the error value.</returns>
    /// <exception cref="NotErr">if the result is a success.</exception>
    /// <example>
    /// <code>
    /// var res = Result&lt;int, string&gt;
    ///     .Failure("Error!");
    /// int val = res.UnwrapErr();
    /// // Outputs: "Error!"
    /// Console.WriteLine(val);    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100); // Simulate async work
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(42)
    ///     .MapFut(AsyncFetchData)
    ///     .UnwrapErr(); // throws!
    ///
    /// // Unreachable! UnwrapErr threw
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public Task<TE> UnwrapErr() => Then(res => res.UnwrapErr());
    
    /// <summary>
    /// Returns the successful value if the result is a success, otherwise throws a NotOk exception with the provided
    /// message.
    /// </summary>
    /// <param name="msg">The message to include in the exception</param>
    /// <returns>A Task which resolves into the successful value.</returns>
    /// <exception cref="NotOk">If the result is an error.</exception>
    /// <example>
    /// <code>
    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100);
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(42)
    ///     .MapFut(AsyncFetchData)
    ///     .Expect("An informative err message");
    ///
    /// // Outputs: 168
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public Task<T> Expect(string msg) => Then(res => res.Expect(msg));
    
    /// <summary>
    /// Returns the successful value if the result is a success, otherwise throws a NotOk exception with a message
    /// generated by the provided function.
    /// </summary>
    /// <param name="op">
    /// The function which takes the error type and returns the message to include in the exception
    /// </param>
    /// <returns>A Task which resolves into the successful value.</returns>
    /// <exception cref="NotOk">If the result is an error.</exception>
    /// <example>
    /// <code>
    /// Result&lt;int, string&gt; CheckId(int id) => id &lt;= 42
    ///     ? Result&lt;int, string&gt;.Success(id)
    ///     : Result&lt;int, string&gt;.Failure("Bad!");
    /// 
    /// async Task&lt;int&gt; AsyncFetchData(int id)
    /// {
    ///     await Task.Delay(100);
    ///     return id * 2;
    /// }
    ///
    /// var result = await CheckId(42)
    ///     .MapFut(AsyncFetchData)
    ///     .ExpectWith(
    ///         e => $"Error! {e}"
    ///     );
    ///
    /// // Outputs: 168
    /// Console.WriteLine(result);
    /// </code>
    /// </example>
    public Task<T> ExpectWith(Func<TE, string> op) => Then(res => res.ExpectWith(op));
    
    /// <summary>
    /// Ignore the values associated with the error, and transform it into a new <see cref="Result{T,TE}"/> based on
    /// the state.
    /// </summary>
    /// <param name="d">The value to associate with the <c>Error</c> state.</param>
    /// <param name="n">The value to associate with the <c>Success</c> state.</param>
    /// <typeparam name="TN">The new success type</typeparam>
    /// <typeparam name="TNe">The new error type</typeparam>
    /// <returns>
    /// The new <see cref="Result{T,TE}"/> with <c>d</c> as the error type and <c>n</c> as the success type
    /// </returns> 
    public FutResult<TN, TNe> As<TN, TNe>(TNe d, TN n) => ThenRes(res => res.As(d, n));
    
    /// <summary>
    /// Inspects the successful value using the provided action.
    /// </summary>
    /// <param name="action">The action to inspect the value.</param>
    /// <returns>The original <see cref="FutResult{T,TE}"/> object.</returns>
    public FutResult<T, TE> Inspect(Action<T> action)
        => ThenRes(res => res.Inspect(action));
    
    /// <summary>
    /// Inspects the error value using the provided action.
    /// </summary>
    /// <param name="action">The action to inspect the error.</param>
    /// <returns>The original <see cref="FutResult{T,TE}"/> object.</returns> 
    public FutResult<T, TE> InspectErr(Action<TE> action) 
        => ThenRes(res => res.InspectErr(action));
    
    /// <summary>
    /// If the error and success type are the same, collapse the result into <c>T</c>
    /// </summary>
    /// <returns>The collapsed value.</returns>
    /// <exception cref="InvalidOperationException"><c>T != TE</c></exception>
    /// <remarks>
    /// In general, it is better to take advantage of methods such as <see cref="MapOr{TNew}"/> or
    /// <see cref="MapOrElse{TNew}"/>, which have similar effect, but cannot result in runtime errors
    /// </remarks>
    public Task<T> Collapse() => Then(res => res.Collapse());
}

///
public class FutResultMethodBuilder<T, TE>
{
    private AsyncTaskMethodBuilder<Result<T, TE>> _builder = AsyncTaskMethodBuilder<Result<T, TE>>.Create();

    ///
    public static FutResultMethodBuilder<T, TE> Create() => new();

    ///
    public FutResult<T, TE> Task => new(_builder.Task);
    ///
    public void SetException(Exception exception) => _builder.SetException(exception);
    ///
    public void SetResult(Result<T, TE> result) => _builder.SetResult(result);
    ///
    public void Start<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine => _builder.Start(ref stateMachine);
    ///
    public void SetStateMachine(IAsyncStateMachine stateMachine) => _builder.SetStateMachine(stateMachine);
    ///
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion
        where TStateMachine : IAsyncStateMachine => _builder.AwaitOnCompleted(ref awaiter, ref stateMachine);
    ///
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion
        where TStateMachine : IAsyncStateMachine => _builder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
}
