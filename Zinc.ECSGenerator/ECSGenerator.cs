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

    private List<(INamedTypeSymbol Type, string Name, bool topLevel)> GetComponentAttributes(INamedTypeSymbol classSymbol, bool useNestedNames)
    {
        var components = new List<(INamedTypeSymbol, string,bool)>();
        
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass!.IsGenericType && attribute.AttributeClass.Name == "ComponentAttribute")
            {
                var componentType = attribute.AttributeClass.TypeArguments[0] as INamedTypeSymbol;
                if (componentType == null) continue;

                string? name = null;
                bool? topLevelAccessor = null;

                // Check for named arguments
                foreach (var namedArgument in attribute.NamedArguments)
                {
                    switch (namedArgument.Key)
                    {
                        case "name":
                            name = namedArgument.Value.Value?.ToString();
                            break;
                        case "topLevelAccessor":
                            topLevelAccessor = (bool?)namedArgument.Value.Value;
                            break;
                    }
                }

                // Check constructor arguments for any values not set by named arguments
                if (name == null || topLevelAccessor == null)
                {
                    if (attribute.ConstructorArguments.Length > 0 && name == null)
                    {
                        name = attribute.ConstructorArguments[0].Value?.ToString();
                    }
                    
                    if (attribute.ConstructorArguments.Length > 1 && topLevelAccessor == null)
                    {
                        topLevelAccessor = (bool?)attribute.ConstructorArguments[1].Value;
                    }
                }

                // Apply defaults if values are still null
                name = name ?? (useNestedNames ? componentType.Name : "");
                topLevelAccessor = topLevelAccessor ?? false;

                components.Add((componentType, name, topLevelAccessor.Value));
            }
        }
        
        return components;
    }

    private string GeneratePartialClass(string nameSpace, string className, List<(INamedTypeSymbol Type, string Name, bool topLevelComponent)> components)
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
            //add in the method that adds the components
            writer.OpenScope("protected override void AddAttributeComponents()");
            var typeNames = new List<string>();
            foreach (var (type, _, _) in components)
            {
                typeNames.Add($"new {type.Name}()");
            }
            writer.AddLine($"ECSEntity.Add({string.Join(",",typeNames)});");
            writer.CloseScope();
        }

        //add component field reference members
        foreach (var (type, name, topLevel) in components)
        {
            if(!topLevel)
            {
                var propName = !string.IsNullOrEmpty(name) ? name : type.Name;
                writer.AddLine($"private _{type.Name} _{type.Name}_{propName};");
                writer.AddLine($"public _{type.Name} {propName} => _{type.Name}_{propName} ??= new _{type.Name}(this);");
                writer.OpenScope($"public class _{type.Name}(Entity e)");
            }

            foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
            {
                //non top-level accessors dont get prepended name, as they are already nested in class
                string accessorName = property.Name;
                string entityRefName = topLevel ? "ECSEntity" : "e.ECSEntity";
                if(topLevel)
                {
                    //top level accessors get prepended name if any
                    accessorName = !string.IsNullOrEmpty(name) ? $"{name}_{property.Name}" : property.Name;
                }
                string typeName = property.Type.ToDisplayString();
                
                if (typeName.StartsWith("System.Action") || typeName.StartsWith("System.Func")) //check if delegate
                {
                    writer.OpenScope($"public {typeName} {accessorName}");
                    writer.OpenScope("get");
                        writer.AddLine($"ref var component = ref {entityRefName}.Get<{type.Name}>();");
                        writer.AddLine($"return component.{property.Name};");
                    writer.CloseScope();
                    writer.OpenScope("set");
                         writer.AddLine($"ref var component = ref {entityRefName}.Get<{type.Name}>();");
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
                            writer.AddLine($"ref var component = ref {entityRefName}.Get<{type.Name}>();");
                            writer.AddLine($"component.{property.Name} = value;");
                            writer.AddLine($"{accessorName.ToLowerInvariant()} = value;");
                        writer.CloseScope();
                    writer.CloseScope();
                }
                writer.AddLine();
            }

            if(!topLevel)
            {
                writer.CloseScope();
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