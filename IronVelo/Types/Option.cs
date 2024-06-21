namespace IronVelo.Types;

/// <summary>
/// Represents an optional value.
/// </summary>
/// <typeparam name="T">The type of the optional value.</typeparam>
public class Option<T>
{
    /// <summary>
    /// A representation of the optional value.
    /// </summary>
    private T? Value { get; }
    
    /// <summary>
    /// Whether the option has a value or not.
    /// </summary>
    private readonly bool _isSome;
    
    private Option(T? value, bool isSome)
    {
        Value = value;
        _isSome = isSome;
    }
    
    /// <summary>
    /// Create a new <see cref="Option{T}"/> with a value (<c>Some</c>).
    /// </summary>
    /// <param name="value">The value to store within the <see cref="Option{T}"/>.</param>
    /// <returns>The <see cref="Option{T}"/> wrapping the value.</returns>
    public static Option<T> Some(T value) => new(value, true);
    /// <summary>
    /// Create a new <see cref="Option{T}"/> in the <c>None</c> state.
    /// </summary>
    /// <returns>The <see cref="Option{T}"/> without any value.</returns>
    public static Option<T> None() => new(default, false);

    /// <summary>
    /// Format the option.
    /// </summary>
    /// <returns>Some(<c>Value</c>) if the option contains a value, None otherwise.</returns>
    public override string ToString() =>
        _isSome ? $"Some({Value})" : "None";
    
    /// <summary>
    /// Apply a function to the inner value of <see cref="Option{T}"/> if the state is currently <c>Some</c>,
    /// potentially transforming the inner type.
    /// </summary>
    /// <param name="mapFunc">The function to apply to the inner value if <c>Some</c>.</param>
    /// <typeparam name="TNew">The new type (the return value of <c>mapFunc</c>.</typeparam>
    /// <returns>The new <see cref="Option{T}"/> type.</returns>
    public Option<TNew> Map<TNew>(Func<T, TNew> mapFunc) =>
        _isSome ? Option<TNew>.Some(mapFunc(Value!)) : Option<TNew>.None();
    
    /// <summary>
    /// Apply a function which results in a new <see cref="Option{T}"/> to the inner value of <see cref="Option{T}"/>
    /// if the state is currently <c>Some</c>, returning a new <see cref="Option{T}"/> and potentially transforming the
    /// inner type.
    /// </summary>
    /// <param name="bindFunc">The function resulting in a new <see cref="Option{T}"/> to the inner value if <c>Some</c>.</param>
    /// <typeparam name="TNew">The new inner type.</typeparam>
    /// <returns>The new <see cref="Option{T}"/> type.</returns>
    public Option<TNew> Bind<TNew>(Func<T, Option<TNew>> bindFunc) =>
        _isSome ? bindFunc(Value!) : Option<TNew>.None();
    
    /// <summary>
    /// Apply an asynchronous function to the inner value of <see cref="Option{T}"/> if the state is currently
    /// <c>Some</c>, potentially transforming the inner type.
    /// </summary>
    /// <param name="mapFunc">The asynchronous function to apply to the inner value if <c>Some</c>.</param>
    /// <typeparam name="TNew">The new type (the return value of <c>mapFunc</c>.</typeparam>
    /// <returns>The new <see cref="Option{T}"/> type wrapped in a <see cref="Task"/>.</returns> 
    public async Task<Option<TNew>> MapFut<TNew>(Func<T, Task<TNew>> mapFunc) =>
        _isSome ? Option<TNew>.Some(await mapFunc(Value!)) : Option<TNew>.None();
    
    /// <summary>
    /// Apply an asynchronous function which results in a new <see cref="Option{T}"/> to the inner value of
    /// <see cref="Option{T}"/> if the state is currently <c>Some</c>, returning a new <see cref="Option{T}"/> and
    /// potentially transforming the inner type.
    /// </summary>
    /// <param name="bindFunc">
    /// The asynchronous function resulting in a new <see cref="Option{T}"/> to the inner value if <c>Some</c>.
    /// </param>
    /// <typeparam name="TNew">The new inner type.</typeparam>
    /// <returns>The new <see cref="Option{T}"/> type wrapped in a <see cref="Task"/>.</returns> 
    public async Task<Option<TNew>> BindFut<TNew>(Func<T, Task<Option<TNew>>> bindFunc) =>
        _isSome ? await bindFunc(Value!) : Option<TNew>.None();
    
    /// <summary>
    /// <see cref="Option{T}"/>'s equivalent of <see cref="Result{T,TE}.Inspect"/>.
    /// </summary>
    /// <param name="action">The action which inspects the inner value if <c>Some</c>.</param>
    /// <returns>The original <see cref="Option{T}"/>.</returns>
    public Option<T> Inspect(Action<T> action)
    {
        if (_isSome)
        {
            action(Value!);
        }
        return this;
    }

    /// <summary>
    /// <see cref="Option{T}"/>'s equivalent of <see cref="Result{T,TE}.InspectErr"/>. 
    /// </summary>
    /// <param name="action">The action to invoke if the option is currently <c>None</c>.</param>
    /// <returns>The original <see cref="Option{T}"/>.</returns>
    public Option<T> OnNone(Action action)
    {
        if (!_isSome)
        {
            action();
        }
        return this;
    }

    /// <summary>
    /// Unwrap the inner value, if the option was <c>None</c> throw an error.
    /// </summary>
    /// <returns>The inner value.</returns>
    /// <exception cref="InvalidOperationException">The <see cref="Option{T}"/> was <c>None</c></exception>
    public T Unwrap() => _isSome ? Value! : throw new InvalidOperationException("Called Unwrap on a None value.");

    /// <returns>True if the <see cref="Option{T}"/> was <c>Some</c>.</returns>
    public bool IsSome() => _isSome;

    /// <returns>True if the <see cref="Option{T}"/> was <c>None</c>.</returns>
    public bool IsNone() => !_isSome;
}
