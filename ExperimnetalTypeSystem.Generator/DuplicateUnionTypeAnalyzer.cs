using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ExperimnetalTypeSystem.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DuplicateUnionTypeAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ONEOF002";

    private static readonly LocalizableString Title = "Duplicate union type declaration";
    private static readonly LocalizableString MessageFormat = "Union type '{0}' has the same types as '{1}'. Duplicate union declarations are not allowed.";
    private static readonly LocalizableString Description = "Two union type declarations cannot contain the same set of types, regardless of order.";
    private const string Category = "Design";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: Description);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeClass, SyntaxKind.ClassDeclaration);
    }

    private static void AnalyzeClass(SyntaxNodeAnalysisContext context)
    {
        var classDecl = (ClassDeclarationSyntax)context.Node;

        // Check if class has ExpermientalTyping attribute
        var classSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
        if (classSymbol is null || !HasExperimentalTypingAttribute(classSymbol))
        {
            return;
        }

        // Collect all union type properties with their type sets
        var unionProperties = new List<(string PropertyName, ImmutableArray<ITypeSymbol> Types, Location Location)>();

        foreach (var member in classDecl.Members.OfType<PropertyDeclarationSyntax>())
        {
            if (context.SemanticModel.GetDeclaredSymbol(member) is not IPropertySymbol propertySymbol)
            {
                continue;
            }

            if (propertySymbol.Type.Name != "UnionType")
            {
                continue;
            }

            if (member.ExpressionBody?.Expression is null)
            {
                continue;
            }

            var types = GetUnionTypes(member.ExpressionBody.Expression, context.SemanticModel);
            if (types.IsEmpty)
            {
                continue;
            }

            unionProperties.Add((propertySymbol.Name, types, member.Identifier.GetLocation()));
        }

        // Check for duplicates
        for (var i = 0; i < unionProperties.Count; i++)
        {
            for (var j = i + 1; j < unionProperties.Count; j++)
            {
                var first = unionProperties[i];
                var second = unionProperties[j];

                // Compare type sets (order-independent)
                if (AreSameTypeSets(first.Types, second.Types))
                {
                    // Report on the second (duplicate) declaration
                    var diagnostic = Diagnostic.Create(
                        Rule,
                        second.Location,
                        second.PropertyName,
                        first.PropertyName);
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static bool HasExperimentalTypingAttribute(INamedTypeSymbol classSymbol)
    {
        foreach (var attribute in classSymbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.Name;
            if (name is "ExpermientalTypingAttribute" or "ExpermientalTyping")
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<ITypeSymbol> GetUnionTypes(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        FlattenOr(expression, semanticModel, builder);
        return builder.ToImmutable();
    }

    private static void FlattenOr(ExpressionSyntax expression, SemanticModel semanticModel, ImmutableArray<ITypeSymbol>.Builder types)
    {
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.BitwiseOrExpression))
        {
            FlattenOr(binary.Left, semanticModel, types);
            FlattenOr(binary.Right, semanticModel, types);
            return;
        }

        if (expression is not TypeOfExpressionSyntax typeOf)
        {
            return;
        }

        var type = semanticModel.GetTypeInfo(typeOf.Type).Type;
        if (type is not null)
        {
            types.Add(type);
        }
    }

    private static bool AreSameTypeSets(ImmutableArray<ITypeSymbol> first, ImmutableArray<ITypeSymbol> second)
    {
        if (first.Length != second.Length)
        {
            return false;
        }

        // Check that every type in first exists in second (order-independent)
        foreach (var type in first)
        {
            if (!second.Any(t => SymbolEqualityComparer.Default.Equals(t, type)))
            {
                return false;
            }
        }

        return true;
    }
}



