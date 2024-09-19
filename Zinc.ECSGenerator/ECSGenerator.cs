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
            
            // Check if any containing type is derived from Entity
            var containingType = classSymbol.ContainingType;
            while (containingType != null)
            {
                if (containingType.Name == BaseClassName)
                    return true;
                containingType = containingType.BaseType;
            }
            
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

    private List<(INamedTypeSymbol Type, string Name, string fullTypeName)> GetAllComponentAttributes(INamedTypeSymbol classSymbol, bool useNestedNames)
    {
        var components = new List<(INamedTypeSymbol, string, string)>();
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

    private List<(INamedTypeSymbol Type, string Name, string FullTypeName)> GetComponentAttributes(INamedTypeSymbol classSymbol, bool useNestedNames)
{
        var components = new List<(INamedTypeSymbol, string, string)>();
        
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

                // Get the full type name including any containing types
                string fullTypeName = GetFullTypeName(componentType);

                components.Add((componentType, name, fullTypeName));
            }
        }
        
        return components;
    }

    private string GetFullTypeName(INamedTypeSymbol type)
    {
        var parts = new List<string>();
        var currentType = type;

        while (currentType != null)
        {
            parts.Add(currentType.Name);
            currentType = currentType.ContainingType;
        }

        parts.Reverse();
        string namespacePart = type.ContainingNamespace.ToDisplayString();
        string typePart = string.Join(".", parts);

        return string.IsNullOrEmpty(namespacePart) ? typePart : $"{namespacePart}.{typePart}";
    }

    private List<(ISymbol Member, string Name, string DefaultValue, bool IsPrimaryCtorParam)> GetComponentMembers(INamedTypeSymbol componentType)
    {
        var members = new List<(ISymbol, string, string, bool)>();
        var primaryCtorParams = new HashSet<string>();

        if (componentType.IsRecord)
        {
            var recordDeclaration = componentType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as RecordDeclarationSyntax;
            if (recordDeclaration?.ParameterList != null)
            {
                foreach (var param in recordDeclaration.ParameterList.Parameters)
                {
                    primaryCtorParams.Add(param.Identifier.Text);
                    string defaultValue = param.Default?.Value.ToString();
                    members.Add((componentType.GetMembers(param.Identifier.Text).FirstOrDefault(), param.Identifier.Text, defaultValue, true));
                }
            }
        }

        foreach (var member in componentType.GetMembers())
        {
            if (member.IsStatic) continue;
            if (primaryCtorParams.Contains(member.Name)) continue; // Skip primary constructor parameters

            if (member is IPropertySymbol property)
            {
                if (property.GetMethod?.DeclaredAccessibility == Accessibility.Public ||
                    property.SetMethod?.DeclaredAccessibility == Accessibility.Public)
                {
                    string defaultValue = GetDefaultValue(property);
                    members.Add((property, property.Name, defaultValue, false));
                }
            }
            else if (member is IFieldSymbol field && field.DeclaredAccessibility == Accessibility.Public)
            {
                string defaultValue = GetDefaultValue(field);
                members.Add((field, field.Name, defaultValue, false));
            }
        }

        return members;
    }

  private string GetDefaultValue(ISymbol symbol)
    {
        // Handle field initializers
        if (symbol is IFieldSymbol field)
        {
            if (field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax fieldDeclarator 
                && fieldDeclarator.Initializer != null)
            {
                return fieldDeclarator.Initializer.Value.ToString();
            }
            return null; // No explicit initializer
        }
        // Handle property initializers
        else if (symbol is IPropertySymbol property)
        {
            if (property.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is PropertyDeclarationSyntax propertyDeclaration 
                && propertyDeclaration.Initializer != null)
            {
                return propertyDeclaration.Initializer.Value.ToString();
            }
        }

        // Handle primary constructors for records only
        var containingType = symbol.ContainingType;
        if (containingType.IsRecord)
        {
            var recordDeclarationSyntax = containingType.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as RecordDeclarationSyntax;
            
            if (recordDeclarationSyntax?.ParameterList != null)
            {
                var parameter = recordDeclarationSyntax.ParameterList.Parameters
                    .FirstOrDefault(p => p.Identifier.Text == symbol.Name);
                if (parameter?.Default is EqualsValueClauseSyntax defaultValue)
                {
                    return defaultValue.Value.ToString();
                }
            }
        }

        return null; // No explicit default value found
    }

    private bool TypeSymbolIsWriteable(INamedTypeSymbol type)
    {
        return !type.IsReadOnly && (type.IsRecord ? !type.IsReferenceType : true);
    }

     private string GeneratePartialClass(string nameSpace, string className, string baseClassName, 
        List<(INamedTypeSymbol Type, string Name, string FullTypeName)> currentClassComponents, 
        List<(INamedTypeSymbol Type, string Name, string FullTypeName)> allComponents)
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

        var isBaseClass = baseClassName == "Object";


        //the base class doesn't inherit from anything explicitly
        writer.OpenScope($"public partial class {className}{(isBaseClass ? "" : $" : {baseClassName}")}");

        if(allComponents.Any())
        {
            var typeTypeNames = allComponents.Select(c => $"typeof({c.FullTypeName})");
            writer.AddLine($"private readonly ComponentType[] EntityArchetype = new ComponentType[]{{{string.Join(",", typeTypeNames)}}};");
            var visibility = isBaseClass ? "protected virtual" : "protected override";
            writer.OpenScope($"{visibility} Arch.Core.Entity CreateECSEntity(World world)");
            writer.AddLine("return world.Create(EntityArchetype);");
            // writer.AddLine("var entity = world.Create(EntityArchetype);");
            // writer.AddLine("AssignDefaultValues();"); dont do this automatically, let the user decide (also need ECSEntity to be assigned before this)
            // writer.AddLine("return entity;");
            writer.CloseScope();

            writer.OpenScope($"{visibility} void AssignDefaultValues()");
            if (!isBaseClass)
            {
                writer.AddLine("base.AssignDefaultValues();");
            }
            
            foreach (var (type, name, fullTypeName) in currentClassComponents)
            {
                if (type.IsRecord)
                {
                    GenerateRecordInitialization(writer, type, name, fullTypeName);
                }
                else if(TypeSymbolIsWriteable(type))
                {
                    foreach (var (member, memberName, defaultValue, _) in GetComponentMembers(type))
                    {
                        string accessorName = !string.IsNullOrEmpty(name) ? $"{name}_{memberName}" : memberName;
                        if (defaultValue != null && CanWrite(member))
                        {
                            writer.AddLine($"{accessorName} = {defaultValue};");
                        }
                    }
                }
                else
                {
                    GenerateNonWriteableComponentInitialization(writer, type);
                }
            }
            
            writer.CloseScope();
        }

        foreach (var (type, name, fullTypeName) in currentClassComponents)
        {
            foreach (var (member, memberName, defaultValue, isPrimaryCtorParam) in GetComponentMembers(type))
            {
                string accessorName = !string.IsNullOrEmpty(name) ? $"{name}_{memberName}" : memberName;
                string typeName = member.GetSymbolType().ToDisplayString();
                
                if (typeName.StartsWith("System.Action") || typeName.StartsWith("System.Func")) //check if delegate
                {
                    GenerateDelegateAccessor(writer, fullTypeName, member, accessorName);
                }
                else
                {
                    GenerateValueTypeAccessor(writer, fullTypeName, member, accessorName);
                }
                writer.AddLine();
            }
        }

        writer.CloseScope();

        return writer.ToString();
    }

    private void GenerateRecordInitialization(Utils.CodeWriter writer, INamedTypeSymbol type, string name, string fullTypeName)
    {
        //this respects the primary ctor
        var members = GetComponentMembers(type);
        var ctorParams = members.Where(m => m.IsPrimaryCtorParam)
                                .Select(m => $"{m.Name}: {m.DefaultValue ?? "default"}");
        var memberInits = members.Where(m => !m.IsPrimaryCtorParam && m.DefaultValue != null && CanWrite(m.Member))
                                 .Select(m => $"{m.Name} = {m.DefaultValue}");

        writer.AddLine($"ECSEntityReference.Entity.Set(new {fullTypeName}({string.Join(", ", ctorParams)}));");
        // dont think we need this if we are doing new() unless we want to set values differet than defaults?
        // if (memberInits.Any())
        // {
        //     writer.AddLine("{");
        //     foreach (var init in memberInits)
        //     {
        //         writer.AddLine($"    {init},");
        //     }
        //     writer.AddLine("});");
        // }
        // else
        // {
        //     writer.AddLine(");");
        // }

        //this makes everything an object initializer set
        // var members = GetComponentMembers(type).Select(m => $"{m.Name}= {m.DefaultValue ?? "default"}");
        // var memini = string.Join(", ", members);

        // writer.AddLine($"ECSEntity.Set(new {type.Name}()");
        // if (members.Any())
        // {
        //     writer.AddLine("{");
        //     writer.AddLine(memini);
        //     writer.AddLine("});");
        // }
        // else
        // {
        //     writer.AddLine(");");
        // }
    }

    private void GenerateNonWriteableComponentInitialization(Utils.CodeWriter writer, INamedTypeSymbol type)
    {
        var members = GetComponentMembers(type);
        var ctorParams = members.Select(m => $"{m.Name}: {m.DefaultValue ?? "default"}");
        writer.AddLine($"ECSEntity.Set(new {type.Name}({string.Join(", ", ctorParams)}));");
    }

    private void GenerateValueTypeAccessor(Utils.CodeWriter writer, string fullTypeName, ISymbol member, string accessorName)
    {
        string typeName = member.GetSymbolType().ToDisplayString();

        writer.OpenScope($"public {typeName} {accessorName}");
        if (CanRead(member))
        {
            writer.AddLine($"get => ECSEntity.Get<{fullTypeName}>().{member.Name};");
        }
        if (CanWrite(member) && TypeSymbolIsWriteable(member.ContainingType as INamedTypeSymbol))
        {
            writer.OpenScope("set");
            writer.AddLine($"ref var component = ref ECSEntity.Get<{fullTypeName}>();");
            writer.AddLine($"component.{member.Name} = value;");
            writer.CloseScope();
        }
        writer.CloseScope();
    }

    private void GenerateDelegateAccessor(Utils.CodeWriter writer, string fullTypeName, ISymbol member, string accessorName)
    {
        string typeName = member.GetSymbolType().ToDisplayString();

        writer.OpenScope($"public {typeName} {accessorName}");
        if (CanRead(member))
        {
            writer.OpenScope("get");
            writer.AddLine($"ref var component = ref ECSEntity.Get<{fullTypeName}>();");
            writer.AddLine($"return component.{member.Name};");
            writer.CloseScope();
        }
        if (CanWrite(member) && TypeSymbolIsWriteable(member.ContainingType as INamedTypeSymbol))
        {
            writer.OpenScope("set");
            writer.AddLine($"ref var component = ref ECSEntity.Get<{fullTypeName}>();");
            writer.AddLine($"component.{member.Name} = value;");
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