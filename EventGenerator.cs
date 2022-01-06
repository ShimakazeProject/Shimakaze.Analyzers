using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Shimakaze.Analyzers;

// See https://github.com/dotnet/roslyn-sdk/tree/main/samples/CSharp/SourceGenerators

[Generator]
public class EventGenerator : ISourceGenerator
{
    private const string FullyQualifiedMetadataName = "Shimakaze.EventAttribute";
    private const string AttributeText = @"//
// Auto Generate By Shimakaze.Analyzers;
//

using System;

namespace Shimakaze
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    [System.Diagnostics.Conditional(""EventAttribute_DEBUG"")]
    public sealed class EventAttribute : Attribute
    {
        public EventAttribute() { }

        public string PropertySummary { get; set; }
        public string PropertyName { get; set; }
        public bool SkipProperty { get; set; } = false;
        public bool IsVirtualProperty { get; set; } = false;

        public string EventSummary { get; set; }
        public string EventName { get; set; }
        public bool SkipEvent { get; set; } = false;

        public string MethodSummary { get; set; }
        public string MethodName { get; set; }
        public bool SkipMethod { get; set; } = false;

        public string EventArgsSummary { get; set; }
        public string EventArgsName { get; set; }
        public bool GenerateEventArgs { get; set; } = false;
    }
}";
    public void Execute(GeneratorExecutionContext context)
    {
        // retrieve the populated receiver 
        if (context.SyntaxContextReceiver is not SyntaxReceiver receiver)
            return;

        // get the added attribute, and INotifyPropertyChanged
        INamedTypeSymbol attributeSymbol = context.Compilation.GetTypeByMetadataName(FullyQualifiedMetadataName)!;

        // group the fields by class, and generate the source
        foreach (IGrouping<INamedTypeSymbol, IFieldSymbol> group in receiver.Fields.GroupBy(f => f.ContainingType))
            InternalEventGenerator.ProcessClass(group.Key, group.ToList(), attributeSymbol, context);

    }

    public void Initialize(GeneratorInitializationContext context)
    {
#if DEBUG && DEBUG_ANALYZER
        if (!Debugger.IsAttached)
            Debugger.Launch();
#endif 
        context.RegisterForPostInitialization((i) => i.AddSource("EventAttribute.g.cs", AttributeText));

        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
    }


    /// <summary>
    /// Created on demand before each generation pass
    /// </summary>
    private class SyntaxReceiver : ISyntaxContextReceiver
    {
        public List<IFieldSymbol> Fields { get; } = new();
        public List<IPropertySymbol> Properties { get; } = new();

        /// <summary>
        /// Called for every syntax node in the compilation, we can inspect the nodes and save any information useful for generation
        /// </summary>
        public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
        {
            switch (context.Node)
            {
                case FieldDeclarationSyntax fieldDeclarationSyntax when fieldDeclarationSyntax.AttributeLists.Any():
                    foreach (VariableDeclaratorSyntax variable in fieldDeclarationSyntax.Declaration.Variables)
                    {
                        // Get the symbol being declared by the field, and keep it if its annotated
                        IFieldSymbol? fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                        if (fieldSymbol!.GetAttributes().Any(ad => ad.AttributeClass!.ToDisplayString() == FullyQualifiedMetadataName))
                            Fields.Add(fieldSymbol);

                    }
                    break;
                default:
                    break;
            }
        }
    }
}
static class InternalEventGenerator
{
    private const string FileHeader = @"// *******************
// * Auto Generated! *
// *  !DO NOT EDIT!  *
// *******************

#if NETCOREAPP3_0_OR_GREATER || NETSTANDARD2_1
#nullable disable
#endif
";

    private static string ChooseName(string fieldName, TypedConstant overridenNameOpt)
    {
        if (!overridenNameOpt.IsNull)
        {
            return overridenNameOpt.Value!.ToString();
        }

        fieldName = fieldName.TrimStart('_');
        if (fieldName.Length == 0)
            return string.Empty;

        if (fieldName.Length == 1)
            return fieldName.ToUpper();

#pragma warning disable IDE0057 // 使用范围运算符
        return $"{fieldName.Substring(0, 1).ToUpper()}{fieldName.Substring(1)}";
#pragma warning restore IDE0057 // 使用范围运算符
    }

    public static void ProcessClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ISymbol attributeSymbol, GeneratorExecutionContext context)
    {
        if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
            return;

        string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

        // begin building the generated source
        StringBuilder properties = new();
        StringBuilder events = new();
        StringBuilder eventMethods = new();
        StringBuilder eventArgs = new();

        // create properties for each field 
        foreach (IFieldSymbol fieldSymbol in fields)
            ProcessField(
                properties,
                events,
                eventMethods,
                eventArgs,
                fieldSymbol,
                attributeSymbol,
                classSymbol,
                context);

        if (properties.Length > 10)
        {
            properties.AppendLine("    }")
                      .AppendLine("}")
                      .Insert(0, @"    {" + Environment.NewLine)
                      .Insert(0, $"    partial class {classSymbol.Name}" + Environment.NewLine)
                      .Insert(0, @"{" + Environment.NewLine)
                      .Insert(0, $"namespace {namespaceName}" + Environment.NewLine)
                      .Insert(0, Environment.NewLine)
                      .Insert(0, "// Properties" + Environment.NewLine)
                      .Insert(0, Environment.NewLine)
                      .Insert(0, FileHeader);

            context.AddSource($"{classSymbol.Name}.g.properties.cs", SourceText.From(properties.ToString(), Encoding.UTF8));
        }
        if (events.Length > 10)
        {
            events.AppendLine("    }")
                  .AppendLine("}")
                  .Insert(0, @"    {" + Environment.NewLine)
                  .Insert(0, $"    partial class {classSymbol.Name}" + Environment.NewLine)
                  .Insert(0, @"{" + Environment.NewLine)
                  .Insert(0, $"namespace {namespaceName}" + Environment.NewLine)
                  .Insert(0, Environment.NewLine)
                  .Insert(0, "// Events" + Environment.NewLine)
                  .Insert(0, Environment.NewLine)
                  .Insert(0, FileHeader);

            context.AddSource($"{classSymbol.Name}.g.events.cs", SourceText.From(events.ToString(), Encoding.UTF8));
        }
        if (eventMethods.Length > 10)
        {
            eventMethods.AppendLine("    }")
                        .AppendLine("}")
                        .Insert(0, @"    {" + Environment.NewLine)
                        .Insert(0, $"    partial class {classSymbol.Name}" + Environment.NewLine)
                        .Insert(0, @"{" + Environment.NewLine)
                        .Insert(0, $"namespace {namespaceName}" + Environment.NewLine)
                        .Insert(0, Environment.NewLine)
                        .Insert(0, "// Event Methods" + Environment.NewLine)
                        .Insert(0, Environment.NewLine)
                        .Insert(0, FileHeader);

            context.AddSource($"{classSymbol.Name}.g.eventMethods.cs", SourceText.From(eventMethods.ToString(), Encoding.UTF8));
        }
        if (eventArgs.Length > 10)
        {
            eventMethods.AppendLine("}")
                        .Insert(0, @"{" + Environment.NewLine)
                        .Insert(0, $"namespace {namespaceName}" + Environment.NewLine)
                        .Insert(0, Environment.NewLine)
                        .Insert(0, "// Event Args" + Environment.NewLine)
                        .Insert(0, Environment.NewLine)
                        .Insert(0, FileHeader);

            context.AddSource($"{classSymbol.Name}.g.eventArgs.cs", SourceText.From(eventArgs.ToString(), Encoding.UTF8));
        }
    }

    private static void ProcessField(
        StringBuilder properties,
        StringBuilder events,
        StringBuilder eventMethods,
        StringBuilder eventArgs,
        IFieldSymbol fieldSymbol,
        ISymbol attributeSymbol,
        INamedTypeSymbol classSymbol,
        GeneratorExecutionContext context)
    {
        // get the name and type of the field
        string fieldName = fieldSymbol.Name;
        ITypeSymbol fieldType = fieldSymbol.Type;

        // get the AutoNotify attribute from the field, and any associated data
        AttributeData attributeData = fieldSymbol.GetAttributes().Single(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass, attributeSymbol));

        var map = attributeData.NamedArguments.ToDictionary(i => i.Key, i => i.Value);
        TypedConstant rnProp = map.GetOrDefault("PropertyName");
        TypedConstant rsmProp = map.GetOrDefault("PropertySummary");
        TypedConstant rskpProp = map.GetOrDefault("SkipProperty");
        TypedConstant rbvProp = map.GetOrDefault("IsVirtualProperty");

        TypedConstant rnEvent = map.GetOrDefault("EventName");
        TypedConstant rsmEvent = map.GetOrDefault("EventSummary");
        TypedConstant rskpEvent = map.GetOrDefault("SkipEvent");

        TypedConstant rnMethod = map.GetOrDefault("MethodName");
        TypedConstant rsmMethod = map.GetOrDefault("MethodSummary");
        TypedConstant rskpMethod = map.GetOrDefault("SkipMethod");

        TypedConstant rnEventArgs = map.GetOrDefault("EventArgsName");
        TypedConstant rsmEventArgs = map.GetOrDefault("EventArgsSummary");
        TypedConstant rmkEventArgs = map.GetOrDefault("GenerateEventArgs");

        string sznProp = ChooseName(fieldName, rnProp);
        string sznEvent = ChooseName($"{fieldName}Changed", rnEvent.IsNull ? rnProp : rnEvent);
        string sznMethod = ChooseName($"On{sznEvent}", rnMethod.IsNull ? rnProp : rnMethod);
        string sznEventArgs = ChooseName($"{sznEvent}EventArgs", rnMethod.IsNull ? rnProp : rnMethod);

        bool bProp = rskpProp.IsNull || !(bool)rskpProp.Value!;
        bool bEvent = rskpEvent.IsNull || !(bool)rskpEvent.Value!;
        bool bMethod = rskpMethod.IsNull || !(bool)rskpMethod.Value!;
        bool bEventArgs = !rmkEventArgs.IsNull && (bool)rmkEventArgs.Value!;

        bool bvProp = !rbvProp.IsNull && (bool)rbvProp.Value!;

        if (sznProp.Length == 0 || sznProp == fieldName)
        {
            //TODO: issue a diagnostic that we can't process this field
            context.ReportDiagnostic(Diagnostic.Create(
                new(
                    "Shimakaze.Analyzers#000",
                    "Property Name \"{1}\" is Equals that Field Name.",
                    sznProp,
                    "Property Name is Equals that Field Name.",
                    DiagnosticSeverity.Warning,
                    true),
                fieldSymbol.Locations.First()));
            return;
        }

        if (bProp)
        {
            if (!string.IsNullOrEmpty(rsmProp.Value as string))
            {
                properties.AppendLine("        /// <summary>")
                          .AppendLine("        /// " + rsmProp.Value)
                          .AppendLine("        /// </summary>");
            }
            properties.AppendLine($"        public {(bvProp ? "virtual " : string.Empty)}{fieldType} {sznProp}")
                      .AppendLine(@"        {")
                      .AppendLine($"            get => this.{fieldName};")
                      .AppendLine($"            set")
                      .AppendLine(@"            {")
                      .AppendLine($"                this.{fieldName} = value;")
                      .AppendLine($"                this.{sznMethod}(value);")
                      .AppendLine(@"            }")
                      .AppendLine(@"        }")
                      .AppendLine();
        }
        if (bEvent)
        {
            if (!string.IsNullOrEmpty(rsmEvent.Value as string))
            {
                events.AppendLine("        /// <summary>")
                      .AppendLine("        /// " + rsmEvent.Value)
                      .AppendLine("        /// </summary>");
            }
            events.AppendLine($"        public event System.EventHandler{(bEventArgs ? $"<{classSymbol.ContainingNamespace.ToDisplayString()}.{sznEventArgs}>" : string.Empty)} {sznEvent};");
        }
        if (bMethod)
        {
            if (!string.IsNullOrEmpty(rsmMethod.Value as string))
            {
                eventMethods.AppendLine("        /// <summary>")
                            .AppendLine("        /// " + rsmMethod.Value)
                            .AppendLine("        /// </summary>");
            }
            eventMethods.AppendLine($"        {(classSymbol.IsSealed ? "private" : "protected virtual")} void {sznMethod}({fieldType} value) => this.{sznEvent}?.Invoke(this, {(bEventArgs ? $"new {classSymbol.ContainingNamespace.ToDisplayString()}.{sznEventArgs}(value)" : "System.EventArgs.Empty")});");
        }
        if (bEventArgs)
        {
            if (!string.IsNullOrEmpty(rsmEventArgs.Value as string))
            {
                eventArgs.AppendLine("        /// <summary>")
                         .AppendLine("        /// " + rsmEventArgs.Value)
                         .AppendLine("        /// </summary>");
            }
            eventArgs.AppendLine($"    public sealed class {sznEventArgs} : System.EventArgs")
                     .AppendLine(@"    {")
                     .AppendLine($"        public {fieldType} Value {{ get; }}")
                     .AppendLine($"        internal {sznEventArgs} ({fieldType} value) => this.Value = value;")
                     .AppendLine(@"    }")
                     .AppendLine();
        }
    }

}