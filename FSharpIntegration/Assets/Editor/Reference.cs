using System.Xml.Linq;

class Reference
{
    public Reference(string include, string hintPath)
    {
        Include = include;
        HintPath = hintPath;
    }

    public string Include { get; set; }
    public string HintPath { get; set; }

    public override int GetHashCode()
    {
        return Include.GetHashCode();
    }

    public XElement ToXElement()
    {
        return new XElement("Reference", new XAttribute("Include", Include), new XElement("HintPath", HintPath));
    }
}