using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GalaxyBudsClient.Generators.Utils;

namespace GalaxyBudsClient.Generators.Enums;

public static class SourceGenerationHelper
{
    public static string Header => 
        """
        // <auto-generated/>
        #nullable enable
        """;
    
    public static string GenerateBindingSource(in EnumToGenerate enumToGenerate)
    {
        var gen = new CodeGenerator();
        gen.AppendLines($"""
                         {Header}
                         using System;
                         using System.Linq;
                         using Avalonia.Markup.Xaml;
                         using GalaxyBudsClient.Model.Attributes;
                         using {enumToGenerate.Namespace};

                         namespace GalaxyBudsClient.Interface.MarkupExtensions;
                         """);
        
        gen.EnterScope($"public class {enumToGenerate.Name}BindingSource : MarkupExtension");
        gen.EnterScope("public override object ProvideValue(IServiceProvider? serviceProvider)");
        
        // Add filters to the EnumBindingSources
        var hasIgnoreDataMemberAttribute = enumToGenerate.Names
            .Any(x => x.Value.AttributeTemplates.Any(n => n.Name == "IgnoreDataMember"));
        var hasRequiresPlatformAttribute = enumToGenerate.Names
            .Any(x => x.Value.AttributeTemplates.Any(n => n.Name == "RequiresPlatform"));
        gen.AppendLines($"""
                         return {enumToGenerate.Namespace}.{enumToGenerate.ExtName}
                             .GetValues()
                         """);
        if(hasIgnoreDataMemberAttribute)
            gen.AppendLine("    .Where(x => !x.HasIgnoreDataMember())");
        if(hasRequiresPlatformAttribute)
            gen.AppendLine("    .Where(x => x.GetRequiresPlatformAttribute()?.IsConditionMet() is true or null)");

        gen.AppendLine("    ;");
        
        gen.LeaveScope();
        gen.LeaveScope();
        return gen.ToString();
    }
    
    public static string GenerateExtensionClass(in EnumToGenerate enumToGenerate)
    {
        var gen = new CodeGenerator();
        gen.AppendLine(Header);
        enumToGenerate.UsingDirectives
            .Concat(new []{ "using System;", "using GalaxyBudsClient.Utils.Interface;" })
            .Select(x => x.Trim())
            .Distinct()
            .ToList()
            .ForEach(gen.AppendLine);
 
        if (!string.IsNullOrEmpty(enumToGenerate.Namespace))
        {
            gen.AppendLine($"namespace {enumToGenerate.Namespace};");
        }

        var fullyQualifiedName = $"global::{enumToGenerate.FullyQualifiedName}";
        gen.AppendLines($"""
                         /// <summary>
                         /// Extension methods for <see cref="{fullyQualifiedName}" />
                         /// </summary>
                         """);
        gen.EnterScope((enumToGenerate.IsPublic ? "public" : "internal") + $" static partial class {enumToGenerate.ExtName}");
        
        // Length
        gen.AppendLines($"""
                         /// <summary>
                         /// The number of members in the enum.
                         /// This is a non-distinct count of defined names.
                         /// </summary>
                         public const int Length = {enumToGenerate.Names.Length};
                         """);
        
        // Attribute extension functions
        foreach (var attrName in enumToGenerate.UsedAttributes)
        {
            // Has{Attribute}
            gen.AppendLines($"""
                             /// <summary>Returns whether the <see cref="{fullyQualifiedName}"/> value has the {attrName} attribute set.</summary>
                             /// <param name="value">The value to check the attribute existence for</param>
                             /// <returns>True, if the value has the attribute set, otherwise false.</returns>
                             """);
            gen.EnterScope($"public static bool Has{attrName}(this {fullyQualifiedName} value) => value switch");

            foreach (var (name, _) in enumToGenerate.Names
                         .Where(x => x.Value.AttributeTemplates.Any(n => n.Name == attrName)))
            {
                gen.AppendLine($"{fullyQualifiedName}.{name} => true,");
            }
            gen.AppendLine("_ => false,");
            
            gen.LeaveScope(";");
            
            var maxArguments = enumToGenerate.Names
                .SelectMany(x => x.Value.AttributeTemplates.Where(t => t.Name == attrName))
                .Select(x => x.Parameters)
                .Max(x => x.Count());
            
            // If the attribute has no parameters, we don't need to generate the GetAttribute methods
            if(maxArguments == 0)
                continue;
                
            // Get{Attribute}Attribute
            gen.AppendLines($"""
                             /// <summary>Returns a new instance of the {attrName} attribute attached to <see cref="{fullyQualifiedName}"/>.</summary>
                             /// <param name="value">The value to retrieve the attribute for</param>
                             /// <returns>The attribute object if the value has an attribute set, otherwise null.</returns>
                             """);
            gen.EnterScope($"public static {attrName}Attribute? Get{attrName}Attribute(this {fullyQualifiedName} value) => value switch");

            foreach (var (name, opts) in enumToGenerate.Names
                         .Where(x => x.Value.AttributeTemplates.Any(n => n.Name == attrName)))
            {
                var parameters = opts.AttributeTemplates.First(x => x.Name == attrName).Parameters;
                gen.AppendLine($"{fullyQualifiedName}.{name} => new {attrName}Attribute({string.Join(", ", parameters)}),");
            }
            gen.AppendLine("_ => null,");
            
            gen.LeaveScope(";");       
            
            // Get{Attribute}
            gen.AppendLines($"""
                             /// <summary>Returns a tuple of constructor parameters passed to the {attrName} attribute attached to <see cref="{fullyQualifiedName}"/>.</summary>
                             /// <param name="value">The value to retrieve the attribute for</param>
                             /// <returns>The tuple of constructor parameters</returns>
                             """);

            var returnType = maxArguments == 1 ? "object" : 
                "(" + string.Join(", ", Enumerable.Repeat("object", maxArguments)) + ")";
            
            // Hardcoded special cases
            if(attrName is "Description" or "LocalizedDescription" or "ModelMetadata")
                returnType = returnType.Replace("object", "string");
            
            gen.EnterScope($"public static {returnType}? Get{attrName}(this {fullyQualifiedName} value) => value switch");

            foreach (var (name, opts) in enumToGenerate.Names
                         .Where(x => x.Value.AttributeTemplates.Any(n => n.Name == attrName)))
            {
                var parameters = opts.AttributeTemplates.First(x => x.Name == attrName).Parameters;
                // ReSharper disable PossibleMultipleEnumeration
                var parameterCount = parameters.Count();
                if(parameterCount < maxArguments)
                    parameters = parameters.Concat(Enumerable.Repeat("default", maxArguments - parameterCount));
                
                gen.AppendLine($"{fullyQualifiedName}.{name} => ({string.Join(", ", parameters)}),");
            }
            gen.AppendLine("_ => null,");
            gen.LeaveScope(";");
          
            // Special case: GetLocalizedDescription
            if(attrName == "LocalizableDescription")
            {
                gen.AppendLines($"""
                                 /// <summary>
                                 /// Returns the localized description of the <see cref="{fullyQualifiedName}"/> value.
                                 /// </summary>
                                 /// <param name="value">The value to retrieve the localized description for</param>
                                 /// <returns>The localized description of the value</returns>
                                 """);
                gen.EnterScope($"public static string GetLocalizedDescription(this {fullyQualifiedName} value) => value switch");
                foreach (var (name, opts) in enumToGenerate.Names
                             .Where(x => x.Value.AttributeTemplates.Any(n => n.Name == attrName)))
                {
                    var parameters = opts.AttributeTemplates.First(x => x.Name == attrName).Parameters;
                    gen.AppendLine($"{fullyQualifiedName}.{name} => Loc.Resolve({parameters.First()}),");
                }
                gen.AppendLine("_ => \"<null>\",");
                gen.LeaveScope(";");
            }
        }
        
        // ToStringFast
        gen.AppendLines($"""
                         /// <summary>
                         /// Returns the string representation of the <see cref="{fullyQualifiedName}"/> value.
                         /// </summary>
                         /// <param name="value">The value to retrieve the string value for</param>
                         /// <returns>The string representation of the value</returns>
                         """);
        gen.EnterScope($"public static string ToStringFast(this {fullyQualifiedName} value) => value switch");
        foreach (var member in enumToGenerate.Names)
        {
            gen.AppendLine($"{fullyQualifiedName}.{member.Key} => nameof({fullyQualifiedName}.{member.Key}),");
        }
        gen.AppendLine("_ => value.ToString(),");
        gen.LeaveScope(";");

        // HasFlagsFast
        if (enumToGenerate.HasFlags)
        {
            gen.AppendLines($$"""
                              /// <summary>
                              /// Determines whether one or more bit fields are set in the current instance.
                              /// Equivalent to calling <see cref="global::System.Enum.HasFlag" /> on <paramref name="value"/>.
                              /// </summary>
                              /// <param name="value">The value of the instance to investigate</param>
                              /// <param name="flag">The flag to check for</param>
                              /// <returns><c>true</c> if the fields set in the flag are also set in the current instance; otherwise <c>false</c>.</returns>
                              /// <remarks>If the underlying value of <paramref name="flag"/> is zero, the method returns true.
                              /// This is consistent with the behaviour of <see cref="global::System.Enum.HasFlag" /></remarks>
                              """);
            gen.AppendLine(
                $"public static bool HasFlagFast(this {fullyQualifiedName} value, {fullyQualifiedName} flag) => flag == 0 ? true : (value & flag) == flag;");
        }
        
        // IsDefined
        gen.AppendLines("""
                        /// <summary>
                        /// Returns a boolean telling whether the given enum value exists in the enumeration.
                        /// </summary>
                        /// <param name="value">The value to check if it's defined</param>
                        /// <returns><c>true</c> if the value exists in the enumeration, <c>false</c> otherwise</returns>
                        """);
        gen.EnterScope($"public static bool IsDefined(this {fullyQualifiedName} value) => value switch");
        foreach (var member in enumToGenerate.Names)
        {
            gen.AppendLine($"{fullyQualifiedName}.{member.Key} => true,");
        }
        gen.AppendLine("_ => false,");
        gen.LeaveScope(";");
        
        // IsDefined(string)
        gen.AppendLines("""
                        /// <summary>
                        /// Returns a boolean telling whether an enum with the given name exists in the enumeration
                        /// </summary>
                        /// <param name="name">The name to check if it's defined</param>
                        /// <returns><c>true</c> if a member with the name exists in the enumeration, <c>false</c> otherwise</returns>
                        """);
        gen.EnterScope($"public static bool IsDefined(string name) => name switch");
        foreach (var member in enumToGenerate.Names)
        {
            gen.AppendLine($"nameof({fullyQualifiedName}.{member.Key}) => true,");
        }
        gen.AppendLine("_ => false,");
        gen.LeaveScope(";");
        
        // TryParse
        gen.AppendLines($"""
                         /// <summary>
                         /// Converts the string representation of the name or numeric value of
                         /// an <see cref="{fullyQualifiedName}" /> to the equivalent instance.
                         /// The return value indicates whether the conversion succeeded.
                         /// </summary>
                         /// <param name="name">The string representation of the enumeration name or underlying value to convert</param>
                         /// <param name="value">When this method returns, contains an object of type
                         /// <see cref="{fullyQualifiedName}" /> whose
                         /// value is represented by <paramref name="value"/> if the parse operation succeeds.
                         /// If the parse operation fails, contains the default value of the underlying type
                         /// of <see cref="{fullyQualifiedName}" />. This parameter is passed uninitialized.</param>
                         /// <param name="ignoreCase"><c>true</c> to read value in case insensitive mode; <c>false</c> to read value in case sensitive mode.</param>
                         /// <returns><c>true</c> if the value parameter was converted successfully; otherwise, <c>false</c>.</returns>
                         """);
        gen.EnterScope($"""
                        public static bool TryParse(
                            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
                            string? name,
                            out {fullyQualifiedName} value,
                            bool ignoreCase = false)
                        """);
        gen.AppendLine("var opt = ignoreCase ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal;");
        gen.EnterScope("switch (name)");
        foreach (var member in enumToGenerate.Names)
        {
            gen.AppendLines($"""
                             case string s when s.Equals(nameof({fullyQualifiedName}.{member.Key}), opt):
                                 value = {fullyQualifiedName}.{member.Key};
                                 return true;
                             """);
        }
        gen.AppendLines($"""
                         case string s when {enumToGenerate.UnderlyingType}.TryParse(name, out var val):
                             value = ({fullyQualifiedName})val;
                             return true;
                         default:
                             value = default;
                             return false;
                         """);
        gen.LeaveScope();
        gen.LeaveScope();
        
        // GetValues
        gen.AppendLines($"""
                         /// <summary>
                         /// Retrieves an array of the values of the members defined in
                         /// <see cref="{fullyQualifiedName}" />.
                         /// Note that this returns a new array with every invocation, so
                         /// should be cached if appropriate.
                         /// </summary>
                         /// <returns>An array of the values defined in <see cref="{fullyQualifiedName}" /></returns>
                         """);
        gen.EnterScope("public static " + fullyQualifiedName + "[] GetValues()");
        gen.EnterScope("return new[]");
        foreach (var member in enumToGenerate.Names)
        {
            gen.AppendLine($"{fullyQualifiedName}.{member.Key},");
        }
        gen.LeaveScope(";");
        gen.LeaveScope();
        
        // GetNames
        gen.AppendLines($"""
                         /// <summary>
                         /// Retrieves an array of the names of the members defined in
                         /// <see cref="{fullyQualifiedName}" />.
                         /// Note that this returns a new array with every invocation, so
                         /// should be cached if appropriate.
                         /// </summary>
                         /// <returns>An array of the names of the members defined in <see cref="{fullyQualifiedName}" /></returns>
                         """);
        gen.EnterScope("public static string[] GetNames()");
        gen.EnterScope("return new[]");
        foreach (var member in enumToGenerate.Names)
        {
            gen.AppendLine($"nameof({fullyQualifiedName}.{member.Key}),");
        }
        gen.LeaveScope(";");
        gen.LeaveScope();
        
        gen.LeaveScope();
        return gen.ToString();
    }
}