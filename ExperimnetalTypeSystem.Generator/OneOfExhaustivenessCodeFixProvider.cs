using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ExperimnetalTypeSystem.Generator;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(OneOfExhaustivenessCodeFixProvider))]
public sealed class OneOfExhaustivenessCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => ImmutableArray.Create(OneOfExhaustivenessAnalyzer.DiagnosticId);

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        var diagnostic = context.Diagnostics.First();
        var diagnosticSpan = diagnostic.Location.SourceSpan;

        // Find the switch keyword token
        var token = root.FindToken(diagnosticSpan.Start);
        var node = token.Parent;

        // Walk up to find switch expression or statement
        while (node is not null && node is not SwitchExpressionSyntax && node is not SwitchStatementSyntax)
        {
            node = node.Parent;
        }

        if (node is null)
        {
            return;
        }

        // Extract missing types from the diagnostic message
        var message = diagnostic.GetMessage();
        var missingTypesStart = message.IndexOf(": ", StringComparison.Ordinal) + 2;
        if (missingTypesStart < 2)
        {
            return;
        }

        var missingTypesString = message.Substring(missingTypesStart);
        var missingTypes = missingTypesString.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);

        if (missingTypes.Length == 0)
        {
            return;
        }

        if (node is SwitchExpressionSyntax switchExpr)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add missing cases",
                    createChangedDocument: ct => AddMissingSwitchExpressionArmsAsync(context.Document, root, switchExpr, missingTypes, ct),
                    equivalenceKey: "AddMissingSwitchExpressionArms"),
                diagnostic);
        }
        else if (node is SwitchStatementSyntax switchStmt)
        {
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Add missing cases",
                    createChangedDocument: ct => AddMissingSwitchStatementCasesAsync(context.Document, root, switchStmt, missingTypes, ct),
                    equivalenceKey: "AddMissingSwitchStatementCases"),
                diagnostic);
        }
    }

    private static Task<Document> AddMissingSwitchExpressionArmsAsync(
        Document document,
        SyntaxNode root,
        SwitchExpressionSyntax switchExpr,
        string[] missingTypes,
        CancellationToken cancellationToken)
    {
        var newArms = new List<SwitchExpressionArmSyntax>();

        // Find the position to insert - before the discard arm if present
        var arms = switchExpr.Arms.ToList();
        var discardIndex = arms.FindIndex(a => a.Pattern is DiscardPatternSyntax);

        foreach (var typeName in missingTypes)
        {
            var variableName = GetVariableName(typeName);

            // Create: TypeName varName => throw new NotImplementedException()
            var arm = SyntaxFactory.SwitchExpressionArm(
                SyntaxFactory.DeclarationPattern(
                    SyntaxFactory.ParseTypeName(typeName),
                    SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(variableName))),
                SyntaxFactory.ThrowExpression(
                    SyntaxFactory.ObjectCreationExpression(
                        SyntaxFactory.ParseTypeName("NotImplementedException"))
                        .WithArgumentList(SyntaxFactory.ArgumentList())));

            newArms.Add(arm);
        }

        // Build new arms list
        var updatedArms = new List<SwitchExpressionArmSyntax>();

        if (discardIndex >= 0)
        {
            // Insert before the discard
            updatedArms.AddRange(arms.Take(discardIndex));
            updatedArms.AddRange(newArms);
            updatedArms.AddRange(arms.Skip(discardIndex));
        }
        else
        {
            // Add at the end
            updatedArms.AddRange(arms);
            updatedArms.AddRange(newArms);
        }

        var newSwitchExpr = switchExpr.WithArms(
            SyntaxFactory.SeparatedList(updatedArms));

        var newRoot = root.ReplaceNode(switchExpr, newSwitchExpr);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    private static Task<Document> AddMissingSwitchStatementCasesAsync(
        Document document,
        SyntaxNode root,
        SwitchStatementSyntax switchStmt,
        string[] missingTypes,
        CancellationToken cancellationToken)
    {
        var newSections = new List<SwitchSectionSyntax>();

        foreach (var typeName in missingTypes)
        {
            var variableName = GetVariableName(typeName);

            // Create: case TypeName varName: throw new NotImplementedException();
            var section = SyntaxFactory.SwitchSection()
                .WithLabels(SyntaxFactory.SingletonList<SwitchLabelSyntax>(
                    SyntaxFactory.CasePatternSwitchLabel(
                        SyntaxFactory.DeclarationPattern(
                            SyntaxFactory.ParseTypeName(typeName),
                            SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(variableName))),
                        SyntaxFactory.Token(SyntaxKind.ColonToken))))
                .WithStatements(SyntaxFactory.SingletonList<StatementSyntax>(
                    SyntaxFactory.ThrowStatement(
                        SyntaxFactory.ObjectCreationExpression(
                            SyntaxFactory.ParseTypeName("NotImplementedException"))
                            .WithArgumentList(SyntaxFactory.ArgumentList()))));

            newSections.Add(section);
        }

        // Find where to insert - before default if present
        var sections = switchStmt.Sections.ToList();
        var defaultIndex = sections.FindIndex(s => s.Labels.Any(l => l is DefaultSwitchLabelSyntax));

        var updatedSections = new List<SwitchSectionSyntax>();

        if (defaultIndex >= 0)
        {
            updatedSections.AddRange(sections.Take(defaultIndex));
            updatedSections.AddRange(newSections);
            updatedSections.AddRange(sections.Skip(defaultIndex));
        }
        else
        {
            updatedSections.AddRange(sections);
            updatedSections.AddRange(newSections);
        }

        var newSwitchStmt = switchStmt.WithSections(SyntaxFactory.List(updatedSections));
        var newRoot = root.ReplaceNode(switchStmt, newSwitchStmt);
        return Task.FromResult(document.WithSyntaxRoot(newRoot));
    }

    private static string GetVariableName(string typeName)
    {
        // Extract simple name from potentially qualified type
        var simpleName = typeName;
        var lastDot = typeName.LastIndexOf('.');
        if (lastDot >= 0)
        {
            simpleName = typeName.Substring(lastDot + 1);
        }

        // Handle generics: List<User> -> listUser
        var genericStart = simpleName.IndexOf('<');
        if (genericStart >= 0)
        {
            var genericEnd = simpleName.LastIndexOf('>');
            if (genericEnd > genericStart)
            {
                var outerType = simpleName.Substring(0, genericStart);
                var innerType = simpleName.Substring(genericStart + 1, genericEnd - genericStart - 1);
                // Remove nested generics for inner type
                var innerGeneric = innerType.IndexOf('<');
                if (innerGeneric >= 0)
                {
                    innerType = innerType.Substring(0, innerGeneric);
                }
                simpleName = outerType + innerType.Replace(",", "").Replace(" ", "");
            }
        }

        // Make first letter lowercase
        if (simpleName.Length > 0)
        {
            simpleName = char.ToLowerInvariant(simpleName[0]) + simpleName.Substring(1);
        }

        return simpleName;
    }
}
