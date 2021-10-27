using System;

namespace Simple.OData.Client
{
    public sealed class GenerateODataClientAttribute : Attribute
    {
        public string Source { get; }

        public GenerateODataClientAttribute(string source)
        {
            Source = source;
        }
    }
}