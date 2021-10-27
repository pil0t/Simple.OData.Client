using System.Linq;
using Microsoft.CodeAnalysis;

namespace Simple.OData.Client.SourceGenerator
{
    internal static class SymbolExtensions
    {
        internal static bool IsAssignableFrom(this ITypeSymbol targetType, ITypeSymbol sourceType, bool exactMatch = false)
        {
            if (targetType is null)
            {
                return false;
            }

            if (exactMatch)
            {
                return SymbolEqualityComparer.Default.Equals(sourceType, targetType);
            }

            while (sourceType != null)
            {
                if (SymbolEqualityComparer.Default.Equals(sourceType, targetType))
                {
                    return true;
                }

                if (targetType.TypeKind == TypeKind.Interface)
                {
                    return sourceType.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, targetType));
                }

                sourceType = sourceType.BaseType;
            }

            return false;
        }
    }
}