using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace AffineTypes;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AffineAttribute : Attribute
{
    public string Message;

    public AffineAttribute(string message = "This instance can only be used once.")
    {
        Message = message;
    }
}

public static class DiagnosticDescriptors
{
    private const string DefaultId = "ATU001";
    private const string DefaultTitle = "Instance used more than once";
    private const string LoopInvariantId = "ATU002";
    private const string LoopInvariantTitle = "Loop violates affine type";
    private const string NoAliasingId = "ATU003";
    private const string NoAliasingTitle = "Affine types cannot be safely aliased";
    
    public static readonly DiagnosticDescriptor AffineDefaultRule = CreateDescriptor(DefaultId, DefaultTitle);
    public static readonly DiagnosticDescriptor AffineLoopRule = CreateDescriptor(LoopInvariantId, LoopInvariantTitle);
    public static readonly DiagnosticDescriptor AffineNoAliasingRule = CreateDescriptor(NoAliasingId, NoAliasingTitle);
    
    private static DiagnosticDescriptor CreateDescriptor(string id, string title) => new(
        id: id,
        title: title,
        messageFormat: "{0}",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true
    );
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class AffineAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.AffineDefaultRule, 
            DiagnosticDescriptors.AffineLoopRule,
            DiagnosticDescriptors.AffineNoAliasingRule
        );

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeOperation, OperationKind.MethodBody, OperationKind.ConstructorBody);
    }
    
    private record IllegalSymbolUsage(
        Location Location
    );

    private class SymbolCtx 
    {
        public SymbolCtx(ITypeSymbol type, bool isUsed = false)
        {
            Type = type;
            IsUsed = isUsed;
        }
        
        public readonly ITypeSymbol Type;
        public bool IsUsed;
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        switch (context.Operation)
        {
            case IMethodBodyOperation methodBody:
                AnalyzeMethodBody(context, methodBody);
                break;
            case IConstructorBodyOperation constructorBody:
                AnalyzeConstructorBody(context, constructorBody);
                break;
        }
    }

    private static void AnalyzeMethodBody(OperationAnalysisContext ctx, IMethodBodyOperation methodBody)
    {
        var body = methodBody.BlockBody ?? methodBody.ExpressionBody;
        var mSym = GetContainingSymbol(ctx);
        if (body == null || mSym == null) return;

        AnalyzeScope(ctx, body, mSym);
    }
    
    private static IMethodSymbol? GetContainingSymbol(OperationAnalysisContext context)
    {
        if (context.ContainingSymbol is IMethodSymbol methodSymbol)
        {
            return methodSymbol;
        }

        return null;
    }

    private static void AnalyzeConstructorBody(OperationAnalysisContext ctx, IConstructorBodyOperation constructorBody)
    {
        var body = constructorBody.BlockBody ?? constructorBody.ExpressionBody;
        var mSym = GetContainingSymbol(ctx);
        if (body == null || mSym == null) return;

        AnalyzeScope(ctx, body, mSym);
    }

    private static void AnalyzeScope(OperationAnalysisContext ctx, IBlockOperation body, IMethodSymbol mSymbol)
    {
        var oneTimes = TrackAffineTypes(body, mSymbol, ctx);

        if (oneTimes.Count != 0)
        {
            TrackUsages(oneTimes, body, ctx);
        }
    }

    private static Dictionary<ISymbol, SymbolCtx> TrackAffineTypes(IBlockOperation body, IMethodSymbol mSymbol, OperationAnalysisContext ctx)
    {
        var iCreations = new Dictionary<ISymbol, SymbolCtx>(SymbolEqualityComparer.Default);
        
        MaybeTrackParams(iCreations, mSymbol);

        foreach (var operation in body.Descendants())
        {
            switch (operation)
            {
                case IVariableDeclarationOperation declarationOperation:
                    MaybeTrackDecl(iCreations, declarationOperation, ctx);
                    break;
            }
        }

        return iCreations;
    }
    
    private static bool IsAffineType(ITypeSymbol typeSymbol) => typeSymbol
        .GetAttributes()
        .Any(attr => attr.AttributeClass?.Name == nameof(AffineAttribute));

    private static void MaybeTrackParams(
        Dictionary<ISymbol, SymbolCtx> iCreations,
        IMethodSymbol mSymbol
    )
    {
        foreach (var param in mSymbol.Parameters.Where(param => IsAffineType(param.Type)))
        {
            iCreations[param] = new SymbolCtx(param.Type);
        }
    }
    
    private static void MaybeTrackDecl(
        Dictionary<ISymbol, SymbolCtx> iCreations,
        IVariableDeclarationOperation decl,
        OperationAnalysisContext ctx
    )
    {
        foreach (var declarator in decl.Declarators)
        {
            var initializer = declarator.Initializer?.Value;
            switch (initializer)
            {
                case null:
                    continue;
                case { Type: { } type } when IsAffineType(type):
                    if (GetSymbol(initializer) is {} sym && iCreations.TryGetValue(sym, out var sCtx))
                    {
                        var diagnostic = Diagnostic.Create(
                            DiagnosticDescriptors.AffineNoAliasingRule, 
                            initializer.Syntax.GetLocation(), 
                            $"'{sym.Name}' cannot be safely aliased: {GetAffineUseMessage(sCtx.Type)}"
                        ); 
                        ctx.ReportDiagnostic(diagnostic); 
                    }
                    iCreations[declarator.Symbol] = new SymbolCtx(type);
                    break;
            }
        }
    }

    private static void WriteDefaultError(string name, ITypeSymbol typeSymbol, Location loc, OperationAnalysisContext ctx)
    {
        var diagnosticMessage = $"'{name}' cannot be reused: {GetAffineUseMessage(typeSymbol)}"; 
        var diagnostic = Diagnostic.Create(DiagnosticDescriptors.AffineDefaultRule, loc, diagnosticMessage); 
        ctx.ReportDiagnostic(diagnostic); 
    }

    private static void TrackUsages(
        Dictionary<ISymbol, SymbolCtx> toTrack,
        IBlockOperation body,
        OperationAnalysisContext ctx 
    )
    {
        foreach (var (symbol, sCtx) in toTrack)
        {
            var illegalUsages = TrackSymbolUsages(symbol, sCtx, body, ctx);
            foreach (var illegal in illegalUsages)
            {
                WriteDefaultError(symbol.Name, sCtx.Type, illegal.Location, ctx);
            }
        }
    }

    private static List<IllegalSymbolUsage> TrackSymbolUsages(
        ISymbol symbol, SymbolCtx ctx, IBlockOperation block, OperationAnalysisContext oCtx
    )
    {
        var maybeIllegal = new List<IllegalSymbolUsage>();
        
        foreach (var op in FlattenRecognize(block))
        {
            maybeIllegal.AddRange(TrackSymbolUsage(symbol, ctx, op, oCtx));
        }

        return maybeIllegal;
    }

    private static List<IllegalSymbolUsage> TrackSymbolUsage(
        ISymbol symbol, SymbolCtx ctx, IOperation op, OperationAnalysisContext oCtx
    )
    {
        return op switch
        {
            IInvocationOperation operation => TrackSymbolUseInvocation(symbol, ctx, operation),
            IReturnOperation operation => TrackSymbolUseReturn(symbol, ctx, operation),
            IAssignmentOperation operation => MaybeIgnoreUsage(symbol, ctx, operation),
            ILoopOperation operation => VerifyLoopBody(symbol, ctx, operation, oCtx),
            IConditionalOperation operation => IfInterpretation(symbol, ctx, operation, oCtx),
            ISwitchOperation operation => SwitchInterpretation(symbol, ctx, operation, oCtx),
            _ => new List<IllegalSymbolUsage>()
        };
    }

    private static List<IllegalSymbolUsage> IfInterpretation(
        ISymbol symbol, SymbolCtx ctx, IConditionalOperation cond, OperationAnalysisContext oCtx)
    {
        return cond.WhenFalse is not { } oBranch 
            ? AnalyzeBlock(symbol, ctx, cond.WhenTrue, oCtx) 
            : BranchInterpretation(symbol, ctx, new List<IOperation> { cond.WhenTrue, oBranch }, oCtx);
    }
    
    private static List<IllegalSymbolUsage> SwitchInterpretation(
        ISymbol symbol, SymbolCtx ctx, ISwitchOperation cond, OperationAnalysisContext oCtx
    )
    {
        return BranchInterpretation(symbol, ctx, cond.Cases, oCtx);
    }

    private static List<IllegalSymbolUsage> BranchInterpretation(
        ISymbol symbol, SymbolCtx ctx, IEnumerable<IOperation> branches, OperationAnalysisContext oCtx
    )
    {
        var mostIllegal = new List<IllegalSymbolUsage>();
        
        foreach (var branch in branches)
        {
            var branchCtx = new SymbolCtx(ctx.Type, ctx.IsUsed);
            var maybeIllegal = AnalyzeBlock(symbol, branchCtx, branch, oCtx);
            
            CheckLegality(symbol, branchCtx, maybeIllegal, oCtx);
            
            if (maybeIllegal.Count > mostIllegal.Count) mostIllegal = maybeIllegal;
            
            ctx.IsUsed |= branchCtx.IsUsed;
        }

        return mostIllegal;
    }

    private static void CheckLegality(
        ISymbol symbol, SymbolCtx sCtx, List<IllegalSymbolUsage> maybeIllegal, OperationAnalysisContext ctx
    )
    {
        foreach (var illegal in maybeIllegal.Skip(1))
        {
            WriteDefaultError(symbol.Name, sCtx.Type, illegal.Location, ctx);
        }
    }

    private static List<IllegalSymbolUsage> VerifyLoopBody(
        ISymbol symbol, SymbolCtx ctx, ILoopOperation op, OperationAnalysisContext oCtx
    )
    {
        /*
         * If we're not ready for usage (reassigned) at the bottom of the loop body then the affine type was violated
         * This means we can have a usage count of 1 (MAX), but IsReset must be true as well
         */
        var wasUsed = ctx.IsUsed;
        var maybeIllegal = AnalyzeBlock(symbol, ctx, op.Body, oCtx);
        var reentryViolation = false;
        
        // Original entrypoint succeeded, but next iterations will fail. Get the locations of the violation.
        if (!wasUsed && ctx.IsUsed)
        {
            maybeIllegal.AddRange(AnalyzeBlock(symbol, ctx, op.Body, oCtx));
            reentryViolation = maybeIllegal.Count != 0;
        }
        
        if (!reentryViolation) return maybeIllegal;
        
        oCtx.ReportDiagnostic(
            Diagnostic.Create(
                DiagnosticDescriptors.AffineLoopRule,
                maybeIllegal.First().Location,
                $"Loop violated affine type `{symbol.Name}`: {GetAffineUseMessage(ctx.Type)}"
            )
        );
        
        // no need to report the failure twice.
        return maybeIllegal.Skip(1).ToList();
    }

    private static List<IllegalSymbolUsage> AnalyzeBlock(
        ISymbol symbol, SymbolCtx ctx, IOperation operation, OperationAnalysisContext oCtx
    )
    {
        var maybeIllegal = new List<IllegalSymbolUsage>();

        foreach (var bOp in FlattenRecognize(operation))
        {
            maybeIllegal.AddRange(TrackSymbolUsage(symbol, ctx, bOp, oCtx));
        }

        return maybeIllegal;
    }
    
    private static IEnumerable<IOperation> FlattenRecognize(IOperation operation)
    {
        var expr = new List<IOperation>();
        var stack = new Stack<IOperation>();
        stack.Push(operation);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            switch (IsConsiderable(current))
            {
                case ConsiderableE.Skip:
                    break;
                case ConsiderableE.Consider:
                    expr.Add(current);
                    break;
                case ConsiderableE.Enumerate:
                default:
                    foreach (var child in current.ChildOperations.Reverse())
                    {
                        stack.Push(child);
                    }
                    break;
            }
        }

        return expr;
    }

    private enum ConsiderableE
    {
        Skip,
        Consider,
        Enumerate
    }

    private static ConsiderableE IsConsiderable(IOperation op)
    {
        return op switch
        {
            IConditionalOperation => ConsiderableE.Consider,
            ISwitchOperation => ConsiderableE.Consider,
            IReturnOperation => ConsiderableE.Consider,
            IInvocationOperation => ConsiderableE.Consider,
            ILoopOperation => ConsiderableE.Consider,
            IAssignmentOperation => ConsiderableE.Consider,
            IVariableDeclarationOperation => ConsiderableE.Skip,
            _ => ConsiderableE.Enumerate 
        };
    }

    private static List<IllegalSymbolUsage> TrackSymbolUseInvocation(
        ISymbol symbol, SymbolCtx ctx, IInvocationOperation invocation
    )
    {
        var maybeIllegal = SymbolInstanceInvokedIllegal(symbol, ctx, invocation);

        foreach (var eOp in EnumerateInvocationOperations(invocation))
        {
            CheckInvArgsSymbol(symbol, ctx, eOp, maybeIllegal);
        }
 
        return maybeIllegal;
    }

    private static List<IllegalSymbolUsage> SymbolInstanceInvokedIllegal(
        ISymbol symbol, SymbolCtx ctx, IInvocationOperation invocation
    )
    {
        if (ctx.IsUsed && IsSymbolInvokedInstance(symbol, invocation) is {} iLoc)
        {
            return new List<IllegalSymbolUsage> { new(iLoc) };
        }

        return new List<IllegalSymbolUsage>();
    }
    
    private static Location? IsSymbolInvokedInstance(ISymbol symbol, IInvocationOperation invocation)
    {
        if (invocation.Instance is {} iSymbol) return OpMatchLocation(symbol, iSymbol);
        return null;
    }

    private static List<IllegalSymbolUsage> TrackSymbolUseReturn(ISymbol symbol, SymbolCtx ctx, IReturnOperation ret)
    {
        var maybeIllegal = new List<IllegalSymbolUsage>();
        if (ret.ReturnedValue != null)
        {
            TrackSymbolUse(symbol, ctx, ret.ReturnedValue, maybeIllegal);
        }
        
        // Returns destroy future paths, so we should reset to express this
        ctx.IsUsed = false;
        return maybeIllegal; 
    }
    
    private static List<IllegalSymbolUsage> MaybeIgnoreUsage(
        ISymbol symbol, SymbolCtx ctx,
        IAssignmentOperation assign
    )
    {
        var maybeIllegal = new List<IllegalSymbolUsage>();
        var wasUsed = ctx.IsUsed;

        foreach(var op in EnumerateInvocationOperations(assign.Value)) 
        {
            CheckInvArgsSymbol(symbol, ctx, op, maybeIllegal);
        }

        if (wasUsed)
        {
            return maybeIllegal;
        }

        ctx.IsUsed = OpMatchLocation(symbol, assign.Target) == null && wasUsed && ctx.IsUsed;
        
        return new List<IllegalSymbolUsage>();
    }
    
    private static IEnumerable<IInvocationOperation> EnumerateInvocationOperations(IOperation operation)
    {
        var invocations = new List<IInvocationOperation>();
        var queue = new Queue<IOperation>();
        queue.Enqueue(operation);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current is IInvocationOperation invocation)
            {
                invocations.Add(invocation);
            }

            foreach (var child in current.ChildOperations)
            {
                queue.Enqueue(child);
            }
        }

        return invocations;
    }
    
    private static void CheckInvArgsSymbol(
        ISymbol symbol, SymbolCtx ctx, IInvocationOperation op, 
        List<IllegalSymbolUsage> maybeIllegal
    )
    {
        foreach (var argument in op.Arguments) 
        {
            TrackSymbolUse(symbol, ctx, argument.Value, maybeIllegal);
        }
    }
    
    private static void TrackSymbolUse(
        ISymbol symbol, SymbolCtx ctx, IOperation op, 
        List<IllegalSymbolUsage> maybeIllegal
    )
    {
        if (OpMatchLocation(symbol, op) is not { } location) return;
        
        if (ctx.IsUsed) maybeIllegal.Add(new IllegalSymbolUsage(location));

        ctx.IsUsed = true;
    }

    private static ISymbol? GetSymbol(IOperation op)
    {
        return op switch
        {
            ILocalReferenceOperation localRef => localRef.Local,
            IParameterReferenceOperation paramRef => paramRef.Parameter,
            _ => null
        }; 
    }

    private static Location? OpMatchLocation(ISymbol symbol, IOperation op)
    {
        return op switch
        {
            ILocalReferenceOperation localRef 
                when SymbolEqualityComparer.Default.Equals(localRef.Local, symbol) => localRef.Syntax.GetLocation(),
            IParameterReferenceOperation paramRef 
                when SymbolEqualityComparer.Default.Equals(paramRef.Parameter, symbol) => paramRef.Syntax.GetLocation(),
            _ => null
        }; 
    }

    private static string GetAffineUseMessage(ITypeSymbol typeSymbol)
    {
        var attribute = typeSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.Name == nameof(AffineAttribute));

        return attribute?.ConstructorArguments.FirstOrDefault().Value as string ?? "This instance can only be used once.";
    }
}