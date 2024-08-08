using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Zinc.ECSGenerator;

[Generator]
public class EcsSourceGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(Compilation, ClassDeclarationSyntax)> classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { Modifiers: var m } && m.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, _) => ((Compilation)ctx.SemanticModel.Compilation, (ClassDeclarationSyntax)ctx.Node))
            .Where(t => t.Item2.BaseList?.Types.Any(type => type.Type.ToString() == "Entity") == true);

        context.RegisterSourceOutput(classDeclarations, 
            (spc, source) => Execute(source.Item1, source.Item2, spc));
    }

    private void Execute(Compilation compilation, ClassDeclarationSyntax classDeclaration, SourceProductionContext context)
    {
        var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null) return;

        string nameSpace = GetNamespace(classSymbol);
        string className = classDeclaration.Identifier.Text;
        
        bool useNestedNames = HasUseNestedComponentMemberNamesAttribute(classSymbol);
        var componentAttributes = GetComponentAttributes(classSymbol, useNestedNames);
        
        string partialClassCode = GeneratePartialClass(nameSpace, className, componentAttributes);
        context.AddSource($"{className}.g.cs", SourceText.From(partialClassCode, Encoding.UTF8));

        // string debugInfo = GenerateDebugInfo(classSymbol, componentAttributes);
        // context.AddSource($"{className}.debug.g.cs", SourceText.From(debugInfo, Encoding.UTF8));
    }

    private string GetNamespace(ISymbol symbol)
    {
        string nameSpace = string.Empty;
        ISymbol currentSymbol = symbol;

        while (currentSymbol != null)
        {
            if (currentSymbol is INamespaceSymbol namespaceSymbol)
            {
                if (!string.IsNullOrEmpty(namespaceSymbol.Name))
                {
                    nameSpace = namespaceSymbol.Name + (string.IsNullOrEmpty(nameSpace) ? string.Empty : "." + nameSpace);
                }
            }
            currentSymbol = currentSymbol.ContainingSymbol;
        }

        return nameSpace;
    }

    private bool HasUseNestedComponentMemberNamesAttribute(INamedTypeSymbol classSymbol)
    {
        return classSymbol.GetAttributes().Any(attr => attr.AttributeClass.Name == "UseNestedComponentMemberNamesAttribute");
    }

    private List<(INamedTypeSymbol Type, string Name)> GetComponentAttributes(INamedTypeSymbol classSymbol, bool useNestedNames)
    {
        var components = new List<(INamedTypeSymbol, string)>();
        
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass!.IsGenericType && attribute.AttributeClass.Name == "ComponentAttribute")
            {
                var componentType = attribute.AttributeClass.TypeArguments[0] as INamedTypeSymbol;
                if (componentType == null) continue;

                string? name = null;

                // Check for named arguments
                foreach (var namedArgument in attribute.NamedArguments)
                {
                    if (namedArgument.Key == "name")
                    {
                        name = namedArgument.Value.Value?.ToString();
                        break;
                    }
                }

                // Check constructor arguments if name is not set
                if (name == null && attribute.ConstructorArguments.Length > 0)
                {
                    name = attribute.ConstructorArguments[0].Value?.ToString();
                }

                // Apply default if value is still null
                name = name ?? (useNestedNames ? componentType.Name : "");

                components.Add((componentType, name));
            }
        }
        
        return components;
    }

    private string GeneratePartialClass(string nameSpace, string className, List<(INamedTypeSymbol Type, string Name)> components)
    {
        var writer = new Utils.CodeWriter();

        writer.AddLine("using Zinc.Core;");
        writer.AddLine("using Arch.Core.Extensions;");
        writer.AddLine("");

        if (!string.IsNullOrEmpty(nameSpace))
        {
            writer.AddLine($"namespace {nameSpace};");
            writer.AddLine();
        }

        writer.OpenScope($"public partial class {className} : Entity");

        if(components.Any())
        {
            writer.OpenScope("protected override void AddAttributeComponents()");
            var typeNames = new List<string>();
            foreach (var (type, _) in components)
            {
                typeNames.Add($"new {type.Name}()");
            }
            writer.AddLine($"ECSEntity.Add({string.Join(",",typeNames)});");
            writer.CloseScope();
        }

        foreach (var (type, name) in components)
        {
            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                string accessorName = !string.IsNullOrEmpty(name) ? $"{name}_{property.Name}" : property.Name;
                string typeName = property.Type.ToDisplayString();
                
                if (typeName.StartsWith("System.Action") || typeName.StartsWith("System.Func")) //check if delegate
                {
                    writer.OpenScope($"public {typeName} {accessorName}");
                    writer.OpenScope("get");
                        writer.AddLine($"ref var component = ref ECSEntity.Get<{type.Name}>();");
                        writer.AddLine($"return component.{property.Name};");
                    writer.CloseScope();
                    writer.OpenScope("set");
                        writer.AddLine($"ref var component = ref ECSEntity.Get<{type.Name}>();");
                        writer.AddLine($"component.{property.Name} = value;");
                    writer.CloseScope();
                    writer.CloseScope();
                }
                else
                {
                    writer.AddLine($"private {typeName} {accessorName.ToLowerInvariant()};");
                    writer.OpenScope($"public {typeName} {accessorName}");
                        writer.AddLine($"get => {accessorName.ToLowerInvariant()};");
                        writer.OpenScope("set");
                            writer.AddLine($"ref var component = ref ECSEntity.Get<{type.Name}>();");
                            writer.AddLine($"component.{property.Name} = value;");
                            writer.AddLine($"{accessorName.ToLowerInvariant()} = value;");
                        writer.CloseScope();
                    writer.CloseScope();
                }
                writer.AddLine();
            }
        }

        writer.CloseScope();

        return writer.ToString();
    }

    private string GenerateDebugInfo(INamedTypeSymbol classSymbol, List<(INamedTypeSymbol Type, string Name)> components)
    {
        var writer = new Utils.CodeWriter();
        writer.AddLine($"// Debug information for {classSymbol.Name}");
        writer.AddLine($"// Attributes: {string.Join(", ", classSymbol.GetAttributes().Select(a => a.AttributeClass.ToDisplayString()))}");
        writer.AddLine($"// Component count: {components.Count}");
        
        foreach (var (type, name) in components)
        {
            writer.AddLine($"// Component: {type.Name}, Name: {name}");
            writer.AddLine($"// Properties: {string.Join(", ", type.GetMembers().OfType<IPropertySymbol>().Select(p => p.Name))}");
        }

        return writer.ToString();
    }
}