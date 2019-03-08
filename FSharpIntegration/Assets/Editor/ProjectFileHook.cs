#if UNITY_EDITOR_WIN
using System.IO;
using System.Text;
//using SyntaxTree.VisualStudio.Unity.Bridge;
using UnityEditor;
 
/// <summary>
/// Provide a hook into Unity's Project File Generation so that StyleCop gets re-added each time
/// </summary>
[InitializeOnLoad]
public class ProjectFileHook
{
    static ProjectFileHook()
    {
//        ProjectFilesGenerator.ProjectFileGeneration += (string name, string content) =>
//        {
//            // parse the document and make some changes
//            UnityEngine.Debug.Log ("ProjectFileGeneration: " + name);

//            var document = XDocument.Parse(content);
//            XNamespace xmlns = "http://schemas.microsoft.com/developer/msbuild/2003";
//            XElement itemGroup = new XElement(xmlns + "ItemGroup");
//            itemGroup.Add(new XElement(xmlns + "Analyzer", new XAttribute("Include", "packages\\StyleCop.Analyzers.1.0.2\\analyzers\\dotnet\\cs\\StyleCop.Analyzers.CodeFixes.dll")));
//            itemGroup.Add(new XElement(xmlns + "Analyzer", new XAttribute("Include", "packages\\StyleCop.Analyzers.1.0.2\\analyzers\\dotnet\\cs\\StyleCop.Analyzers.dll")));
//            document.Root.Add(itemGroup);
//            document.Root.Add(new XElement(xmlns + "ItemGroup", new XElement(xmlns + "AdditionalFiles", new XAttribute("Include", "stylecop.json"))));
// 
//            // save the changes using the Utf8StringWriter
//            var str = new Utf8StringWriter();
//            document.Save(str);
 
//            return "".ToString();
//        };
    }
 
}
#endif

// necessary for XLinq to save the xml project file in utf8
public class Utf8StringWriter : StringWriter
{
    public override Encoding Encoding
    {
        get { return Encoding.UTF8; }
    }
}