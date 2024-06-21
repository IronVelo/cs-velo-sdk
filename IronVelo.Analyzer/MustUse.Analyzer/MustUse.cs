using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace MustUse;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Property, Inherited = false)]
public sealed class MustUseAttribute : Attribute
{
    public string Message;

    public MustUseAttribute(string? message = null)
    {
        Message = message ?? "The return value must be used";
    }
}

public static class DiagnosticDescriptors
{
    private const string DiagnosticId = "MU001"; 

    public static readonly DiagnosticDescriptor MustUseReturnValueRule = new(
        id: DiagnosticId,
        title: "Must Use Return Value",
        messageFormat: "{0}",
        category: "Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
}

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MustUseReturnValueAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(DiagnosticDescriptors.MustUseReturnValueRule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterOperationAction(AnalyzeOperation, OperationKind.Invocation, OperationKind.ObjectCreation, OperationKind.PropertyReference, OperationKind.Await);
    }

    private void AnalyzeOperation(OperationAnalysisContext context)
    {
        switch (context.Operation)
        {
            case IInvocationOperation invocationOperation:
                AnalyzeInvocationOperation(context, invocationOperation);
                break;
            case IObjectCreationOperation objectCreationOperation:
                AnalyzeObjectCreationOperation(context, objectCreationOperation);
                break;
            case IPropertyReferenceOperation propertyReferenceOperation:
                AnalyzePropertyReferenceOperation(context, propertyReferenceOperation);
                break;
            case IAwaitOperation awaitOperation:
                AnalyzeAwaitOperation(context, awaitOperation);
                break;
            case IMemberReferenceOperation memberReferenceOperation:
                AnalyzeMemberReferenceOperation(context, memberReferenceOperation);
                break;
        }
    }

    private static void AnalyzeInvocationOperation(OperationAnalysisContext context, IInvocationOperation operation)
    {
        var methodSymbol = operation.TargetMethod;

        if (HasMustUseAttribute(methodSymbol, out var methodMessage))
        {
            ReportDiagnosticIfNeeded(context, operation, methodMessage, methodSymbol.Name);
        }

        if (HasMustUseAttribute(methodSymbol.ReturnType, out var returnTypeMessage))
        {
            ReportDiagnosticIfNeeded(context, operation, returnTypeMessage, methodSymbol.ReturnType.Name);
        }
    }

    private static void AnalyzeObjectCreationOperation(OperationAnalysisContext context, IObjectCreationOperation operation)
    {
        var typeSymbol = operation.Type;

        if (typeSymbol != null && HasMustUseAttribute(typeSymbol, out var message))
        {
            ReportDiagnosticIfNeeded(context, operation, message, typeSymbol.Name);
        }
    }
    
    private static void AnalyzeAwaitOperation(OperationAnalysisContext context, IAwaitOperation operation)
    {
        if (operation.Type is {} ty && HasMustUseAttribute(ty, out var tyMessage))
        {
            ReportDiagnosticIfNeeded(context, operation, tyMessage, ty.Name);
        }
        else if (operation.Operation is IInvocationOperation invocationOperation)
        {
            AnalyzeInvocationOperation(context, invocationOperation);
        }
    }

    private static void AnalyzeMemberReferenceOperation(OperationAnalysisContext context, IMemberReferenceOperation operation)
    {
        if (operation.Instance is IInvocationOperation invocationOperation)
        {
            AnalyzeInvocationOperation(context, invocationOperation);
        }
    }
    
    private static void AnalyzePropertyReferenceOperation(OperationAnalysisContext context, IPropertyReferenceOperation operation)
    {
        var propertySymbol = operation.Property;

        if (HasMustUseAttribute(propertySymbol, out var message))
        {
            ReportDiagnosticIfNeeded(context, operation, message, propertySymbol.Name);
        }
    }

    private static void ReportDiagnosticIfNeeded(OperationAnalysisContext context, IOperation operation, string message, string name)
    {
        if (operation.Parent is not IExpressionStatementOperation) return;
        var diagnosticMessage = string.IsNullOrEmpty(message) 
            ? $"The return value of '{name}' must be used." 
            : message;

        var diagnostic = Diagnostic.Create(DiagnosticDescriptors.MustUseReturnValueRule, operation.Syntax.GetLocation(), diagnosticMessage);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool HasMustUseAttribute(ISymbol symbol, out string message)
    {
        message = null!;
        foreach (var attribute in symbol.GetAttributes().Where(attribute => attribute.AttributeClass?.Name == "MustUseAttribute"))
        {
            if (attribute.ConstructorArguments.First() is { Value: { } msg })
            {
                message = (msg as string)!;
            }
            return true;
        }
        return false;
    }
}