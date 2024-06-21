using System;
using System.Linq;

namespace ResultAble;

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ResultAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class OkAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
public sealed class ErrorAttribute : Attribute { }

[Generator]
public class ResultAble : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var syntaxTrees = context.Compilation.SyntaxTrees;
        foreach (var tree in syntaxTrees)
        {
            var root = tree.GetRoot();
            var records = root.DescendantNodes();

            foreach (var record in records)
            {
                var model = context.Compilation.GetSemanticModel(tree);
                if (model.GetDeclaredSymbol(record) is not INamedTypeSymbol classSymbol || !classSymbol.GetAttributes()
                        .Any(a => a.AttributeClass?.Name == nameof(ResultAttribute))) continue;
                var source = GenerateClassSource(classSymbol);
                context.AddSource($"{classSymbol.Name}_ResultGen.cs", SourceText.From(source, Encoding.UTF8));
            }
        }
    }

    private static string GenerateClassSource(INamedTypeSymbol classSymbol)
    {
        var namespaceName = classSymbol.ContainingNamespace.ToDisplayString();
        var className = classSymbol.Name;
        var classVisibility = GetClassVisibility(classSymbol);
        var properties = classSymbol.GetMembers().OfType<IPropertySymbol>().ToList();

        var okProperty = properties
            .FirstOrDefault(p => p
                .GetAttributes()
                .Any(a => a.AttributeClass?.Name == nameof(OkAttribute)));
        
        var errorProperty = properties
            .FirstOrDefault(p => p
                .GetAttributes()
                .Any(a => a.AttributeClass?.Name == nameof(ErrorAttribute)));

        if (okProperty == null && errorProperty != null)
        {
            return OkNone(namespaceName, classVisibility, className, errorProperty);
        }

        if (okProperty != null && errorProperty == null)
        {
            return ErrNone(namespaceName, classVisibility, className, okProperty);
        }

        if (okProperty == null || errorProperty == null)
            return string.Empty;

        return StdImpl(namespaceName, classVisibility, className, okProperty, errorProperty);
    }

    private static string StdImpl(
        string nSpace, string cVis, string cName, IPropertySymbol okProp, IPropertySymbol errProp
    )
    {
        return $@"
namespace {nSpace}
{{
    {cVis} partial record {cName}
    {{
        #pragma warning disable CS0472
        public IronVelo.Types.Result<{okProp.Type.ToDisplayString()}, {errProp.Type.ToDisplayString()}> ToResult()
        {{
            if ({okProp.Name} != null)
            {{
                return IronVelo.Types.Result<{okProp.Type.ToDisplayString()}, {errProp.Type.ToDisplayString()}>.Success({okProp.Name});
            }}
            else if ({errProp.Name} != null)
            {{
                return IronVelo.Types.Result<{okProp.Type.ToDisplayString()}, {errProp.Type.ToDisplayString()}>.Failure({errProp.Name});
            }}
            else
            {{
                throw new IronVelo.Exceptions.RequestError(
                    IronVelo.Exceptions.RequestErrorKind.Deserialization, ""Both Ok and Error properties are null"");
            }}
        }}
        #pragma warning restore CS0472
    }}
}}
";
    }
    
    private static string OkNone(string nSpace, string cVis, string cName, IPropertySymbol errProp)
    {
        return $@"
namespace {nSpace}
{{
    {cVis} partial record {cName}
    {{
        #pragma warning disable CS0472
        public IronVelo.Types.Result<IronVelo.Types.None, {errProp.Type.ToDisplayString()}> ToResult()
        {{
            return {errProp.Name} is {{ }} __error 
                ? IronVelo.Types.Result<IronVelo.Types.None, {errProp.Type.ToDisplayString()}>.Failure(__error)
                : IronVelo.Types.Result<IronVelo.Types.None, {errProp.Type.ToDisplayString()}>.Success(new IronVelo.Types.None());
        }}
        #pragma warning restore CS0472
    }}
}}
"; 
    }

    private static string ErrNone(string nSpace, string cVis, string cName, IPropertySymbol okProp)
    {
        return $@"
namespace {nSpace}
{{
    {cVis} partial record {cName}
    {{
        #pragma warning disable CS0472
        public IronVelo.Types.Result<{okProp.Type.ToDisplayString()}, IronVelo.Types.None> ToResult()
        {{
            return {okProp.Name} is {{ }} __ok 
                ? IronVelo.Types.Result<{okProp.Type.ToDisplayString()}, IronVelo.Types.None>.Success(__ok)
                : IronVelo.Types.Result<{okProp.Type.ToDisplayString()}, IronVelo.Types.None>.Failure(new IronVelo.Types.None());
        }}
        #pragma warning restore CS0472
    }}
}}
";  
    }

    private static string GetClassVisibility(INamedTypeSymbol classSymbol)
    {
        return classSymbol.DeclaredAccessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.Private => "private",
            _ => "internal"
        };
    }
}
