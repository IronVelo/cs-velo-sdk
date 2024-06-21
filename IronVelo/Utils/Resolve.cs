namespace IronVelo.Utils;

/// <summary>
/// A utility for resolving tasks within <c>Task.ContinueWith</c>.
/// </summary>
public static class Resolve
{
    /// <summary>
    /// Resolve the <see cref="Task{TResult}"/>'s value within a <c>ContinueWith</c>.
    /// </summary>
    /// <param name="task">The <see cref="Task{TResult}"/> to resolve.</param>
    /// <typeparam name="TR">The result of the <see cref="Task{TResult}"/></typeparam>
    /// <returns><c>TR</c>, the value which the <see cref="Task{TResult}"/> resolves into.</returns>
    /// <exception cref="AggregateException">
    /// The task was canceled. The <see cref="AggregateException.InnerExceptions"/> collection contains a
    /// <see cref="TaskCanceledException"/> object. -or- An exception was thrown during the execution of the task.
    /// The <see cref="AggregateException.InnerExceptions"/> collection contains information about the exception or
    /// exceptions.
    /// </exception>
    /// <exception cref="OperationCanceledException">The task was cancelled.</exception>
    public static TR Get<TR>(Task<TR> task) =>
        task.IsCompletedSuccessfully
            ? task.Result
            : task.IsFaulted
                ? throw task.Exception!
                : task.IsCanceled
                    ? throw new OperationCanceledException("Task was canceled.")
                    // Non-documented as should be impossible. Should undergo further review to ensure that is accurate
                    // as we could reduce a branch. Though, in this circumstance, performance isn't too much of a 
                    // concern. Hot path is prioritized in this function.
                    : throw new InvalidOperationException("Task is not completed.");
}