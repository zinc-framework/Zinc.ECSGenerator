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
    public static string BaseClassName = "Entity";
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(Compilation, ClassDeclarationSyntax)> classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax { Modifiers: var m } && m.Any(SyntaxKind.PartialKeyword),
                transform: static (ctx, _) => ((Compilation)ctx.SemanticModel.Compilation, (ClassDeclarationSyntax)ctx.Node))
            .Where(t => IsEntityDerived(t.Item2, t.Item1));

        context.RegisterSourceOutput(classDeclarations, 
            (spc, source) => Execute(source.Item1, source.Item2, spc));
    }

    private static bool IsEntityDerived(ClassDeclarationSyntax classDeclaration, Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
        
        while (classSymbol != null)
        {
            if (classSymbol.Name == BaseClassName)
                return true;
            classSymbol = classSymbol.BaseType;
        }
        
        return false;
    }

    private void Execute(Compilation compilation, ClassDeclarationSyntax classDeclaration, SourceProductionContext context)
    {
        var semanticModel = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
        var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);

        if (classSymbol == null) return;

        string nameSpace = GetNamespace(classSymbol);
        string className = classDeclaration.Identifier.Text;
        string baseClassName = classSymbol.BaseType?.Name ?? BaseClassName;
        
        bool useNestedNames = HasUseNestedComponentMemberNamesAttribute(classSymbol);
        var currentClassComponents = GetComponentAttributes(classSymbol, useNestedNames);
        var allComponents = GetAllComponentAttributes(classSymbol, useNestedNames);
        
        if (currentClassComponents.Any() || className == BaseClassName)
        {
            string partialClassCode = GeneratePartialClass(nameSpace, className, baseClassName, currentClassComponents, allComponents);
            context.AddSource($"{className}.g.cs", SourceText.From(partialClassCode, Encoding.UTF8));
        }
    }

    private List<(INamedTypeSymbol Type, string Name)> GetAllComponentAttributes(INamedTypeSymbol classSymbol, bool useNestedNames)
    {
        var components = new List<(INamedTypeSymbol, string)>();
        while (classSymbol != null)
        {
            components.AddRange(GetComponentAttributes(classSymbol, useNestedNames));
            if (classSymbol.Name == BaseClassName)
                break;
            classSymbol = classSymbol.BaseType;
        }
        return components.Distinct().ToList();
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

    private List<(ISymbol Member, string Name)> GetComponentMembers(INamedTypeSymbol componentType)
    {
        var members = new List<(ISymbol, string)>();

        foreach (var member in componentType.GetMembers())
        {
            if (member is IPropertySymbol property)
            {
                // Include properties with at least one public accessor
                if (property.GetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    property.SetMethod?.DeclaredAccessibility == Accessibility.Public)
                {
                    members.Add((property, property.Name));
                }
            }
            else if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public)
            {
                // Include public fields
                members.Add((field, field.Name));
            }
        }

        return members;
    }

    private string GeneratePartialClass(string nameSpace, string className, string baseClassName, 
        List<(INamedTypeSymbol Type, string Name)> currentClassComponents, 
        List<(INamedTypeSymbol Type, string Name)> allComponents)
    {
        var writer = new Utils.CodeWriter();

        writer.AddLine("using Zinc.Core;");
        writer.AddLine("using Arch.Core;");
        writer.AddLine("using Arch.Core.Extensions;");
        writer.AddLine("using Arch.Core.Utils;");
        writer.AddLine("");

        if (!string.IsNullOrEmpty(nameSpace))
        {
            writer.AddLine($"namespace {nameSpace};");
            writer.AddLine();
        }

        if(baseClassName == "Object")
        {
            //the base class doesn't inherit from anything explicitly
            writer.OpenScope($"public partial class {className}");
        }
        else
        {
            writer.OpenScope($"public partial class {className} : {baseClassName}");
        }

        if(allComponents.Any())
        {
            var typeTypeNames = allComponents.Select(c => $"typeof({c.Type.Name})");
            writer.AddLine($"private readonly ComponentType[] EntityArchetype = new ComponentType[]{{{string.Join(",", typeTypeNames)}}};");
            writer.OpenScope("private Arch.Core.Entity CreateECSEntity(World world)");
            writer.AddLine("return world.Create(EntityArchetype);");
            writer.CloseScope();
        }

        foreach (var (type, name) in currentClassComponents)
        {
            foreach (var (member, memberName) in GetComponentMembers(type))
            {
                string accessorName = !string.IsNullOrEmpty(name) ? $"{name}_{memberName}" : memberName;
                string typeName = member.GetSymbolType().ToDisplayString();
                
                if (typeName.StartsWith("System.Action") || typeName.StartsWith("System.Func")) //check if delegate
                {
                    GenerateDelegateAccessor(writer, type, member, accessorName);
                }
                else
                {
                    GenerateValueTypeAccessor(writer, type, member, accessorName);
                }
                writer.AddLine();
            }
        }

        writer.CloseScope();

        return writer.ToString();
    }

    private void GenerateDelegateAccessor(Utils.CodeWriter writer, INamedTypeSymbol type, ISymbol member, string accessorName)
    {
        string typeName = member.GetSymbolType().ToDisplayString();

        writer.OpenScope($"public {typeName} {accessorName}");
        if (CanRead(member))
        {
            writer.OpenScope("get");
            writer.AddLine($"ref var component = ref ECSEntity.Get<{type.Name}>();");
            writer.AddLine($"return component.{member.Name};");
            writer.CloseScope();
        }
        if (CanWrite(member))
        {
            writer.OpenScope("set");
            writer.AddLine($"ref var component = ref ECSEntity.Get<{type.Name}>();");
            writer.AddLine($"component.{member.Name} = value;");
            writer.CloseScope();
        }
        writer.CloseScope();
    }

    private void GenerateValueTypeAccessor(Utils.CodeWriter writer, INamedTypeSymbol type, ISymbol member, string accessorName)
    {
        string typeName = member.GetSymbolType().ToDisplayString();

        writer.AddLine($"private {typeName} {accessorName.ToLowerInvariant()};");
        writer.OpenScope($"public {typeName} {accessorName}");
        if (CanRead(member))
        {
            writer.AddLine($"get => {accessorName.ToLowerInvariant()};");
        }
        if (CanWrite(member))
        {
            writer.OpenScope("set");
            writer.AddLine($"ref var component = ref ECSEntity.Get<{type.Name}>();");
            writer.AddLine($"component.{member.Name} = value;");
            writer.AddLine($"{accessorName.ToLowerInvariant()} = value;");
            writer.CloseScope();
        }
        writer.CloseScope();
    }

    private bool CanRead(ISymbol member)
    {
        if (member is IPropertySymbol property)
        {
            return property.GetMethod?.DeclaredAccessibility == Accessibility.Public;
        }
        return member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public;
    }

    private bool CanWrite(ISymbol member)
    {
        if (member is IPropertySymbol property)
        {
            return property.SetMethod?.DeclaredAccessibility == Accessibility.Public;
        }
        return member is IFieldSymbol field && !field.IsReadOnly && field.DeclaredAccessibility == Accessibility.Public;
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

public static class SymbolExtensions
{
    public static ITypeSymbol GetSymbolType(this ISymbol symbol)
    {
        return symbol switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => throw new ArgumentException("Symbol must be a field or property", nameof(symbol))
        };
    }
}