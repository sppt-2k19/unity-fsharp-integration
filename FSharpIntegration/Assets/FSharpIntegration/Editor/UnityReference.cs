using System.Xml.Linq;

namespace FSharpIntegration.Editor
{
    class UnityReference
    {
        public UnityReference(string include, string hintPath)
        {
            Include = include;
            HintPath = hintPath;
        }

        public readonly string Include;
        public readonly string HintPath;

        public override int GetHashCode()
        {
            return Include.GetHashCode();
        }

        public XElement ToXElement()
        {
            return new XElement("Reference", new XAttribute("Include", Include), new XElement("HintPath", HintPath));
        }
    }
}