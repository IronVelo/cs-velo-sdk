namespace IronVelo.Types;

/// <summary>
/// Represents nothing.
/// </summary>
public record None
{
    /// <summary>
    /// Represent <see cref="None"/> as a successful <see cref="Result{T,TE}"/> where <c>T</c> is 
    /// <see cref="None"/>
    /// </summary>
    /// <typeparam name="TE">The error type (<c>TE</c>) of the <see cref="Result{T,TE}"/></typeparam>
    /// <returns>The success <see cref="Result{T,TE}"/> where <c>T</c> is <see cref="None"/></returns>
    public static Result<None, TE> AsOk<TE>()
    {
        return Result<None, TE>.Success(new None());
    }

    /// <summary>
    /// Represent <see cref="None"/> as a failed <see cref="Result{T,TE}"/> where <c>TE</c> is 
    /// <see cref="None"/>
    /// </summary>
    /// <typeparam name="T">The success type (<c>T</c>) of the <see cref="Result{T,TE}"/></typeparam>
    /// <returns>The failure <see cref="Result{T,TE}"/> where <c>TE</c> is <see cref="None"/></returns>
    public static Result<T, None> AsErr<T>()
    {
        return Result<T, None>.Failure(new None());
    }
};
