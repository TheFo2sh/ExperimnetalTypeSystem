using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ExperimnetalTypeSystem.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneOfExhaustivenessAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "ONEOF001";

    private static readonly LocalizableString Title = "Non-exhaustive switch on OneOf type";
    private static readonly LocalizableString MessageFormat = "Switch is not exhaustive. Missing type(s): {0}";
    private static readonly LocalizableString Description = "Switch expressions/statements on GetValue() with [OneOf] attribute should handle all possible types.";
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

        context.RegisterSyntaxNodeAction(AnalyzeSwitchExpression, SyntaxKind.SwitchExpression);
        context.RegisterSyntaxNodeAction(AnalyzeSwitchStatement, SyntaxKind.SwitchStatement);
    }

    private static void AnalyzeSwitchExpression(SyntaxNodeAnalysisContext context)
    {
        var switchExpr = (SwitchExpressionSyntax)context.Node;

        var oneOfTypes = GetOneOfTypesFromExpression(switchExpr.GoverningExpression, context.SemanticModel);
        if (oneOfTypes.IsDefaultOrEmpty)
        {
            return;
        }

        var (handledTypes, hasDiscard) = GetHandledTypesFromSwitchExpression(switchExpr, context.SemanticModel);
        
        // If there's a discard pattern (_ =>), the switch is considered exhaustive
        if (hasDiscard)
        {
            return;
        }
        
        var missingTypes = GetMissingTypes(oneOfTypes, handledTypes);

        if (missingTypes.Length > 0)
        {
            var diagnostic = Diagnostic.Create(
                Rule,
                switchExpr.SwitchKeyword.GetLocation(),
                string.Join(", ", missingTypes.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))));
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static void AnalyzeSwitchStatement(SyntaxNodeAnalysisContext context)
    {
        var switchStmt = (SwitchStatementSyntax)context.Node;

        var oneOfTypes = GetOneOfTypesFromExpression(switchStmt.Expression, context.SemanticModel);
        if (oneOfTypes.IsDefaultOrEmpty)
        {
            return;
        }

        var (handledTypes, hasDefault) = GetHandledTypesFromSwitchStatement(switchStmt, context.SemanticModel);
        
        // If there's a default case, the switch is considered exhaustive
        if (hasDefault)
        {
            return;
        }
        
        var missingTypes = GetMissingTypes(oneOfTypes, handledTypes);

        if (missingTypes.Length > 0)
        {
            var diagnostic = Diagnostic.Create(
                Rule,
                switchStmt.SwitchKeyword.GetLocation(),
                string.Join(", ", missingTypes.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))));
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static ImmutableArray<ITypeSymbol> GetOneOfTypesFromExpression(ExpressionSyntax expression, SemanticModel semanticModel)
    {
        // Handle: obj.GetValue() or variable from obj.GetValue()
        IMethodSymbol? methodSymbol = null;

        if (expression is InvocationExpressionSyntax invocation)
        {
            // Direct call: union.GetValue() switch { ... }
            methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        }
        else if (expression is IdentifierNameSyntax identifier)
        {
            // Variable: var x = union.GetValue(); switch (x) { ... }
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (symbol is ILocalSymbol local)
            {
                var declarator = local.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax() as VariableDeclaratorSyntax;

                if (declarator?.Initializer?.Value is InvocationExpressionSyntax varInvocation)
                {
                    methodSymbol = semanticModel.GetSymbolInfo(varInvocation).Symbol as IMethodSymbol;
                }
            }
        }

        if (methodSymbol is null || methodSymbol.Name != "GetValue")
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        // Find [OneOf] attribute on the method
        var oneOfAttribute = methodSymbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "OneOfAttribute" or "OneOf");

        if (oneOfAttribute is null)
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        // Extract types from attribute constructor arguments
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();

        foreach (var arg in oneOfAttribute.ConstructorArguments)
        {
            if (arg.Kind == TypedConstantKind.Array)
            {
                foreach (var element in arg.Values)
                {
                    if (element.Value is ITypeSymbol typeSymbol)
                    {
                        builder.Add(typeSymbol);
                    }
                }
            }
            else if (arg.Value is ITypeSymbol typeSymbol)
            {
                builder.Add(typeSymbol);
            }
        }

        return builder.ToImmutable();
    }

    private static (ImmutableArray<ITypeSymbol> HandledTypes, bool HasDiscard) GetHandledTypesFromSwitchExpression(SwitchExpressionSyntax switchExpr, SemanticModel semanticModel)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        var hasDiscard = false;

        foreach (var arm in switchExpr.Arms)
        {
            var pattern = arm.Pattern;

            // Check for discard pattern: _ => ...
            if (pattern is DiscardPatternSyntax)
            {
                hasDiscard = true;
                continue;
            }

            var typeSymbol = GetTypeFromPattern(pattern, semanticModel);
            if (typeSymbol is not null)
            {
                builder.Add(typeSymbol);
            }
        }

        return (builder.ToImmutable(), hasDiscard);
    }

    private static (ImmutableArray<ITypeSymbol> HandledTypes, bool HasDefault) GetHandledTypesFromSwitchStatement(SwitchStatementSyntax switchStmt, SemanticModel semanticModel)
    {
        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();
        var hasDefault = false;

        foreach (var section in switchStmt.Sections)
        {
            foreach (var label in section.Labels)
            {
                // Check for default case
                if (label is DefaultSwitchLabelSyntax)
                {
                    hasDefault = true;
                    continue;
                }

                if (label is CasePatternSwitchLabelSyntax patternLabel)
                {
                    var typeSymbol = GetTypeFromPattern(patternLabel.Pattern, semanticModel);
                    if (typeSymbol is not null)
                    {
                        builder.Add(typeSymbol);
                    }
                }
            }
        }


        return (builder.ToImmutable(), hasDefault);
    }

    private static ITypeSymbol? GetTypeFromPattern(PatternSyntax pattern, SemanticModel semanticModel)
    {
        return pattern switch
        {
            // Type pattern: User u => ...
            DeclarationPatternSyntax declarationPattern =>
                semanticModel.GetTypeInfo(declarationPattern.Type).Type,

            // Type pattern without variable: User => ...
            TypePatternSyntax typePattern =>
                semanticModel.GetTypeInfo(typePattern.Type).Type,

            // Constant pattern with typeof: case typeof(User): (less common)
            ConstantPatternSyntax constantPattern when constantPattern.Expression is TypeOfExpressionSyntax typeOfExpr =>
                semanticModel.GetTypeInfo(typeOfExpr.Type).Type,

            // Recursive pattern: User { } => ...
            RecursivePatternSyntax recursivePattern when recursivePattern.Type is not null =>
                semanticModel.GetTypeInfo(recursivePattern.Type).Type,

            _ => null
        };
    }

    private static ImmutableArray<ITypeSymbol> GetMissingTypes(
        ImmutableArray<ITypeSymbol> requiredTypes,
        ImmutableArray<ITypeSymbol> handledTypes)
    {

        var builder = ImmutableArray.CreateBuilder<ITypeSymbol>();

        foreach (var required in requiredTypes)
        {
            var isHandled = handledTypes.Any(h =>
                SymbolEqualityComparer.Default.Equals(h, required) ||
                IsAssignableTo(required, h));

            if (!isHandled)
            {
                builder.Add(required);
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsAssignableTo(ITypeSymbol source, ITypeSymbol target)
    {
        if (SymbolEqualityComparer.Default.Equals(source, target))
        {
            return true;
        }

        // Check if target is a base type of source
        var current = source.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, target))
            {
                return true;
            }
            current = current.BaseType;
        }

        // Check interfaces
        foreach (var iface in source.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, target))
            {
                return true;
            }
        }

        return false;
    }
}

