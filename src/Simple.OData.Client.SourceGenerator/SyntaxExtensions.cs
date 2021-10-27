using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Simple.OData.Client.SourceGenerator
{
    public static class SyntaxExtensions
    {
        internal static bool ContainsAttributeType(
            this SyntaxList<AttributeListSyntax> attributes,
            SemanticModel semanticModel,
            INamedTypeSymbol attributeType,
            bool exactMatch = false)
            => attributes.Any(
                list => list.Attributes.Any(
                    attrbute => attributeType.IsAssignableFrom(
                        semanticModel.GetTypeInfo(attrbute)
                            .Type,
                        exactMatch)));
    }
}