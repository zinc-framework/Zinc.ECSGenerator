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
public class ECSGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<(Compilation, ClassDeclarationSyntax)> classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax,
                transform: static (ctx, _) => ((Compilation)ctx.SemanticModel.Compilation, (ClassDeclarationSyntax)ctx.Node))
            .Where(t => t.Item2.BaseList?.Types.Any(type => type.Type.ToString() == "BaseEntity") == true);

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
        var componentAttributes = GetComponentAttributes(classSymbol);
        
        string partialClassCode = GeneratePartialClass(nameSpace, className, componentAttributes);
        context.AddSource($"{className}.g.cs", SourceText.From(partialClassCode, Encoding.UTF8));

        foreach (var component in componentAttributes)
        {
            string componentCode = GenerateComponentStruct(nameSpace, component);
            context.AddSource($"{component.Type}.g.cs", SourceText.From(componentCode, Encoding.UTF8));
        }
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

    private List<(string Type, string Name, bool NestDeclaration, List<(string Name, ITypeSymbol Type)> Properties)> GetComponentAttributes(INamedTypeSymbol classSymbol)
    {
        var components = new List<(string, string, bool, List<(string, ITypeSymbol)>)>();
        
        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass.BaseType?.Name == "BaseComponentAttribute")
            {
                string typeName = attribute.AttributeClass.Name.Replace("Attribute", "");
                string name = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
                
                bool nestDeclaration = false; // Default value
                var nestDeclarationArg = attribute.NamedArguments.FirstOrDefault(na => na.Key == "nestTypeName");
                if (nestDeclarationArg.Key != null)
                {
                    nestDeclaration = (bool)nestDeclarationArg.Value.Value;
                }

                var properties = attribute.AttributeClass.GetMembers()
                    .OfType<IPropertySymbol>()
                    .Select(p => (p.Name, p.Type))
                    .ToList();
                
                components.Add((typeName, name, nestDeclaration, properties));
            }
        }
        
        return components;
    }

    private string GeneratePartialClass(string nameSpace, string className, List<(string Type, string Name, bool NestDeclaration, List<(string Name, ITypeSymbol Type)> Properties)> components)
    {
        var writer = new Utils.CodeWriter();

        writer.AddLine($"using Zinc.Core;");

        if (!string.IsNullOrEmpty(nameSpace))
        {
            writer.AddLine($"namespace {nameSpace};");
        }

        writer.OpenScope($"public partial class {className} : BaseEntity");

        foreach (var (type, name, nestDeclaration, properties) in components)
        {
            foreach (var (propName, propType) in properties)
            {
                string propertyName = nestDeclaration ? $"{type}_{propName}" : propName;
                string typeName = propType.ToDisplayString();
                
                bool isDelegate = typeName.StartsWith("System.Action") || typeName.StartsWith("System.Func");

                if (isDelegate)
                {
                    writer.OpenScope($"public {typeName} {propertyName}");
                    writer.AddLine($"get => Get<{type}>(\"{name}\").{propName};");
                    writer.OpenScope("set");
                    writer.AddLine($"var component = Get<{type}>(\"{name}\");");
                    writer.AddLine($"component.{propName} = value;");
                    writer.AddLine($"Set(\"{name}\", component);");
                    writer.CloseScope();
                    writer.CloseScope();
                }
                else
                {
                    //real
                    // writer.OpenScope($"public {typeName} {propertyName}");
                    // writer.AddLine($"get => Get<{type}>(\"{name}\").{propName};");
                    // writer.AddLine($"set => Set(\"{name}\", Get<{type}>(\"{name}\") with {{ {propName} = value }});");
                    // writer.CloseScope();

                    //debug
                    writer.AddLine($"private {typeName} {propertyName.ToLowerInvariant()};");
                    writer.OpenScope($"public {typeName} {propertyName}");
                    writer.AddLine($"get => {propertyName.ToLowerInvariant()};");
                    writer.AddLine($"set => {propertyName.ToLowerInvariant()} = value;");
                    writer.CloseScope();
                }
                writer.AddLine();
            }
        }

        writer.CloseScope();

        return writer.ToString();
    }

    private string GenerateComponentStruct(string nameSpace, (string Type, string Name, bool NestDeclaration, List<(string Name, ITypeSymbol Type)> Properties) component)
    {
        var writer = new Utils.CodeWriter();

        if (!string.IsNullOrEmpty(nameSpace))
        {
            writer.AddLine($"using Zinc.Core;");
            writer.AddLine($"namespace {nameSpace};");
        }

        // NOTE: right now we prepend an underscore to the type name to avoid conflicts with the class name - could do something nicer
        writer.OpenScope($"public record struct _{component.Type}");

        foreach (var (propName, propType) in component.Properties)
        {
            string typeName = propType.ToDisplayString();
            writer.AddLine($"public {typeName} {propName} {{ get; set; }}");
        }

        writer.CloseScope();

        return writer.ToString();
    }
}