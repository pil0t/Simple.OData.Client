using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.OData.Edm;
using Microsoft.OData.Edm.Csdl;
using Microsoft.OData.Edm.Validation;
using Simple.OData.Client.SourceGenerator;

namespace OData.SG
{
    [Generator]
    public class ODSG : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        private static IEdmModel ReadModel(string xml)
        {
            using (var reader = new StringReader(xml))
            {
                IEdmModel model;
                IEnumerable<EdmError> errors;
                if (CsdlReader.TryParse(XmlReader.Create(reader), out model, out errors))
                {
                    return model;
                }
                return null;
            }
        }

        public void Execute(GeneratorExecutionContext context)
        {
            var metadatas = new List<(string, string, string)>();
            var generateDataBuilderAttributeType =
                context.Compilation.GetTypeByMetadataName("Simple.OData.Client.GenerateODataClientAttribute");
            if (generateDataBuilderAttributeType is null)
            {
                return;
            }

            foreach (var inputDocument in context.Compilation.SyntaxTrees)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                var typeNodes = inputDocument.GetRoot()
                    .DescendantNodesAndSelf(
                        n =>
                            n is CompilationUnitSyntax || n is NamespaceDeclarationSyntax || n is TypeDeclarationSyntax)
                    .OfType<TypeDeclarationSyntax>();

                var semanticModel = context.Compilation.GetSemanticModel(inputDocument);

                foreach (var typeNode in typeNodes)
                {
                    if (!typeNode.AttributeLists.ContainsAttributeType(
                        semanticModel,
                        generateDataBuilderAttributeType,
                        exactMatch: true))
                    {
                        continue;
                    }

                    var typeSymbol = semanticModel.GetDeclaredSymbol(typeNode) as ITypeSymbol;

                    if (typeSymbol == null)
                    {
                        continue;
                    }

                    var url = typeSymbol.GetAttributeProperty("Simple.OData.Client.GenerateODataClientAttribute");
                    metadatas.Add((url, typeSymbol.Name, typeSymbol.ContainingNamespace.Name));
                }
            }

            var generated = new HashSet<string>();
            foreach (var (url, typeName, namespaceName) in metadatas)
            {
                using var wc = new WebClient();
                var meta = wc.DownloadString(url);
                var model = ReadModel(meta);

                foreach (var element in model.SchemaElements)
                {
                    switch (element)
                    {
                        case IEdmEnumType enumType:
                            GenerateEnum(context, enumType, generated);
                            break;
                        case IEdmComplexType complexType:
                            GenerateComplexType(context, complexType, generated);
                            break;
                        case IEdmEntityType entityType:
                            GenerateEntityType(context, entityType, generated);
                            break;
                        case IEdmEntityContainer container:
                            GenerateContainer(context, container, typeName, namespaceName, model.SchemaElements);
                            break;
                    }
                }
            }
        }

        private void GenerateContainer(GeneratorExecutionContext context,
            IEdmEntityContainer container,
            string serviceName,
            string namespaceName,
            IEnumerable<IEdmSchemaElement> modelSchemaElements)
        {
            var es = container.EntitySets();
            var methods = "";
            foreach (var set in es)
            {
                var openTypeSuffix = "";
                if (set.EntityType()
                    .IsOpen)
                {
                    openTypeSuffix = ".WithProperties(x => x.Properties)";
                }

                methods += $@"
        public IBoundClient<{set.EntityType().FullTypeName()}> {set.Name} => client.For<{set.EntityType().FullTypeName()}>(""{set.Name}""){openTypeSuffix};
";
            }

            var functions = "";
            foreach (var import in modelSchemaElements.OfType<IEdmFunction>().Where(x=>!x.IsBound))
            {
                var parameters = "";

                var formatted =
                    import.Parameters.Select(parameter => GetCLRType(parameter.Type) + " " + parameter.Name);
                parameters = string.Join(",", formatted);
                var clrType = GetCLRType(import.ReturnType);
                var r = "";
                if (clrType.Contains("ICollection<"))
                {
                    r = $".ExecuteAsArrayAsync{clrType.Replace("ICollection", "")}();";
                }
                else
                {
                    r = $".ExecuteAsSingleAsync<{clrType}>();";
                }
                var parametersSet = string.Join(
                    ",\n",
                    import.Parameters.Select(parameter => parameter.Name + " = " + parameter.Name));
                functions += @$"
         public async Task<{clrType}> {import.Name}({parameters})
        {{
            return await client.Unbound()
                .Function(""{import.Name}"")
                .Set(
                    new
                    {{
                        {parametersSet}
                    }})
                {r}
        }}
";
            }

            var tmpl = $@"using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Simple.OData.Client;
namespace {namespaceName}
{{
  public partial class {serviceName}
  {{
        private ODataClient client;

        public {serviceName}(ODataClient client)
        {{
            this.client = client;
        }}

    {methods}

    {functions}
  }}  
}}
";
            context.AddSource($"JibbleAPI.{serviceName}", SourceText.From(tmpl, Encoding.UTF8));
        }

        private void GenerateEnum(GeneratorExecutionContext context, IEdmEnumType enumType, HashSet<string> hashSet)
        {
            if (enumType.Namespace.StartsWith("System"))
            {
                Console.WriteLine("Skipping " + enumType.Name);
                return;
            }
            var tmpl = $@"using System;
namespace {enumType.Namespace}
{{
  {(enumType.IsFlags ? "[Flags]" : null)}
  public enum {enumType.Name}
  {{
    {string.Join(",\n    ", enumType.Members.Select(x=>$"{x.Name} = {x.Value.Value}" ))}
  }}  
}}
";
            var typeName = enumType.Namespace + "." + enumType.Name;
            if (hashSet.Contains(typeName))
            {
                if (typeName.Length > 50)
                return;
            }

            hashSet.Add(typeName);
            context.AddSource(typeName, SourceText.From(tmpl, Encoding.UTF8));
        }

        private void GenerateEntityType(
            GeneratorExecutionContext context,
            IEdmEntityType entityType,
            HashSet<string> hashSet)
        {
            CultureInfo cultureInfo = Thread.CurrentThread.CurrentCulture;
            TextInfo textInfo = cultureInfo.TextInfo;
            var props = "";
            foreach (var property in entityType.DeclaredProperties)
            {
                var type = GetCLRType(property.Type);
                var annotations = "";
                if (property.IsKey())
                {
                    annotations += "[Key]";
                }

                props += $@"
{annotations}
    public {type} {ToTitleCase(property.Name)} {{get;set;}} 
";
            }

            if (entityType.IsOpen && (entityType.BaseType?.IsOpen != true))
            {
                props += @"
        public Dictionary<string, object> Properties { get; set; }
";
            }

            var baseType = "";
            if (entityType.BaseType != null)
            {
                baseType = " : " + entityType.BaseType.FullTypeName();
            }
            var tmpl = $@"using System;
using Microsoft.OData.Edm;
using Microsoft.Spatial;
using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
namespace {entityType.Namespace}
{{
  public partial class {entityType.Name} {baseType}
  {{
    {props}
  }}  
}}
";

            var typeName = entityType.Namespace + "." + entityType.Name;
            if (hashSet.Contains(typeName))
            {
                return;
            }

            hashSet.Add(typeName);
            context.AddSource(typeName, SourceText.From(tmpl, Encoding.UTF8));
        }

        private static string GetCLRType(IEdmTypeReference edmTypeReference)
        {
            var typeDefinition = edmTypeReference.Definition;
            var type = "";
            if (typeDefinition is IEdmPrimitiveType primitiveType)
            {
                type = primitiveType.Name;

                if (type == "Binary")
                {
                    type = "byte[]";
                }

                if (type == "Duration")
                {
                    type = "TimeSpan";
                }

                if (edmTypeReference.IsNullable && type != "String" && type != "byte[]")
                {
                    type += "?";
                }
            }
            else if (typeDefinition is IEdmType edmType)
            {
                type = edmType.FullTypeName()
                    .Replace("Edm.", "");
            }

            type = type.Replace("(", "<")
                .Replace(")", ">");

            if (type.StartsWith("Collection<"))
            {
                type = "I" + type;
            }

            return type;
        }

        private void GenerateComplexType(
            GeneratorExecutionContext context,
            IEdmComplexType complexType,
            HashSet<string> hashSet)
        {
            CultureInfo cultureInfo = Thread.CurrentThread.CurrentCulture;
            TextInfo textInfo = cultureInfo.TextInfo;
            var props = "";
            foreach (var property in complexType.DeclaredProperties)
            {
                var type = GetCLRType(property.Type);

                props += $@"
    public {type} {ToTitleCase(property.Name)} {{get;set;}} 
";
            }
            var tmpl = $@"using System;
using Microsoft.OData.Edm;
using Microsoft.Spatial;
using System.Collections.Generic;
namespace {complexType.Namespace}
{{
  public partial class {complexType.Name}
  {{
    {props}
  }}  
}}
";

            var typeName = complexType.Namespace + "." + complexType.Name;
            if (hashSet.Contains(typeName))
            {
                return;
            }

            hashSet.Add(typeName);

            context.AddSource(typeName, SourceText.From(tmpl, Encoding.UTF8));
        }

        private string ToTitleCase(string propertyName)
        {
            return char.ToUpper(propertyName[0]) + propertyName.Substring(1);
        }

        private string ReplaceType(string value)
        {
            value = value.Replace("Duration", nameof(TimeSpan));

            if (value.StartsWith("Collection("))
            {
                value = "I" + value;
                var items = value.Split('(', ')');
                var n = items[1]
                    .Split('.')
                    .Last();
                value = "ICollection<" + n + ">";
            }

            return value;
        }
    }
}