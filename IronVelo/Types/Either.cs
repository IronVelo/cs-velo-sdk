namespace IronVelo.Types;

/// <summary>
/// Denotes which side in <see cref="Either{LT, RT}"> currently has a value.
/// </summary>
public enum EitherE 
{
    /// <summary>
    /// The left side carries a value
    /// </summary>
    Left,
    /// <summary>
    /// The right side carries a value
    /// </summary>
    Right
}

/// <summary>
/// Denotes one of two possibilities
/// </summary>
public record Either<LT, RT>  
{
    /// <summary>
    /// Which side of <see cref="Either{LT, RT}"/> currently holds a value
    /// </summary>
    public readonly EitherE Side;

    /// <summary>
    /// The value for the left side, which always exists if the <c>Side</c> is <c>Left</c>.
    /// </summary>
    public readonly LT? LeftV;
    /// <summary>
    /// The value for the right side, which always exists if the <c>Side</c> is <c>Right</c>.
    /// </summary>
    public readonly RT? RightV;

    private Either(EitherE side, LT? left, RT? right) 
    {
        Side = side;
        LeftV = left;
        RightV = right;
    }

    /// <summary>
    /// Create a new Either type with the value on the left side.
    /// </summary>
    public static Either<LT, RT> Left(LT left) => new(EitherE.Left, left, default);
    /// <summary>
    /// Create a new Either type with the value on the right side.
    /// </summary>
    public static Either<LT, RT> Right(RT right) => new(EitherE.Right, default, right);

    /// <summary>
    /// Apply some function to the left value if the <c>Side</c> is currently <c>Left</c>.
    /// </summary>
    public Either<LNT, RT> MapLeft<LNT>(Func<LT, LNT> map) 
    {
        if (Side == EitherE.Left) {
            return Either<LNT, RT>.Left(map(LeftV!));
        } else {
            return Either<LNT, RT>.Right(RightV!);
        }
    }

    /// <summary>
    /// Apply some function to the right value if the <c>Side</c> is currently <c>Right</c>.
    /// </summary>
    public Either<LT, RNT> MapRight<RNT>(Func<RT, RNT> map)
    {
        if (Side == EitherE.Right) {
            return Either<LT, RNT>.Right(map(RightV!));
        } else {
            return Either<LT, RNT>.Left(LeftV!);
        }
    }
}
