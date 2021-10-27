using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Simple.OData.Client.SourceGenerator
{
    public static class AttributeExtensions
    {
        public static string GetAttributeProperty(
            this ITypeSymbol assembly,
            string attributeName)
        {
            AttributeData attributeData = assembly
                .GetAttributes()
                .FirstOrDefault(x => x.AttributeClass?.ToString() == $"{attributeName}");

            if (attributeData == null)
            {
                return null;
            }

            ImmutableArray<TypedConstant> attributeArguments = attributeData.ConstructorArguments;

            return attributeArguments.FirstOrDefault().Value as string;
        }
    }
}