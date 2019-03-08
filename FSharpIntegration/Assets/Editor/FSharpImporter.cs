using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;


public class FSharpImporter : AssetPostprocessor
{
	const bool DEBUG = true;
	
	private const string MenuItemRecompile = "F#/Compile F#";
	private const string MenuItemAutoCompile = "F#/Enable Auto-compile";
	private const string MenuItemUseDotnet = "F#/Use dotnet compiler";
	
	private static bool _compiling = false;
	private static bool _autoRecompileEnabled = EditorPrefs.GetBool(MenuItemAutoCompile);
	private static bool _useDotnet = EditorPrefs.GetBool(MenuItemUseDotnet);
	private static readonly XNamespace Xmlns = "http://schemas.microsoft.com/developer/msbuild/2003";
	private static readonly Regex MatchReferences =
		new Regex("<Reference Include=\"([^\"]+)\">\\s*<HintPath>([^<]+)<\\/HintPath>\\s*<\\/Reference>", RegexOptions.Compiled);

	public FSharpImporter()
	{
		Menu.SetChecked(MenuItemAutoCompile, EditorPrefs.GetBool(MenuItemAutoCompile));
		Menu.SetChecked(MenuItemUseDotnet, EditorPrefs.GetBool(MenuItemUseDotnet));
	}

	static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
	{
		if (!_autoRecompileEnabled) return;
		if (DEBUG) Debug.Log ("Starting automatic recompilation");
		InvokeCompiler();
	}

	[MenuItem("Assets/Create/F# Script")]
	static void AddFSharpScript()
	{
		
	}
	
	[MenuItem(MenuItemRecompile, false, 1)]
	static void InvokeCompiler()
	{
		if (_compiling) return;
		_compiling = true;
		try
		{
			var dir = Directory.GetCurrentDirectory();
			
			var fsProjects = Directory.EnumerateFiles(dir, "*.fsproj", SearchOption.AllDirectories);
			Task.Run(() => {
				var references = ExtractUnityReferences(dir);

				foreach (var project in fsProjects)
				{
					EnsureReferences(project, references);
					Compile(dir, project);
				}
			});
		} 
		catch (Exception e) {
			Debug.LogError(e);
		}
		_compiling = false;
	}
	[MenuItem(MenuItemRecompile, true, 1)]
	static bool IsReadyForCompilation()
	{
		return !_compiling;
	}
	
	[MenuItem(MenuItemAutoCompile, false, 52)]
	private static void ToggleAutoCompile()
	{
		_autoRecompileEnabled = !_autoRecompileEnabled;
		Menu.SetChecked(MenuItemAutoCompile, _autoRecompileEnabled);
		EditorPrefs.SetBool(MenuItemAutoCompile, _autoRecompileEnabled);
	}
	
	[MenuItem(MenuItemUseDotnet, false, 51)]
	private static void ToggleDotnet()
	{
		_useDotnet = !_useDotnet;
		Menu.SetChecked(MenuItemUseDotnet, _useDotnet);
		EditorPrefs.SetBool(MenuItemUseDotnet, _useDotnet);
	}
	
	private static Lazy<ReferenceContainer> ExtractUnityReferences(string dir)
	{
		return new Lazy<ReferenceContainer>(() =>
		{
			var started = DateTime.UtcNow;
			var unityCsProjects = Directory.EnumerateFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);

			if (!unityCsProjects.Any()) throw new FileNotFoundException("No Unity projects to copy references from found. Please add a C# script, open it, and try again");
			
			var allReferences = new HashSet<Reference>();
			foreach (var project in unityCsProjects)
			{
				var csProjectContent = File.ReadAllText(project);
				var matches = MatchReferences.Matches(csProjectContent);
				foreach (Match match in matches)
				{
					var include = match.Groups[1].Value;
					var hintPath = match.Groups[2].Value;
					allReferences.Add(new Reference(include, hintPath));
				}
			}


			var unityEngine = allReferences.FirstOrDefault(r => r.Include == "UnityEngine");
			var unityEditor = allReferences.FirstOrDefault(r => r.Include == "UnityEditor");
		
			allReferences.Remove(unityEngine);
			allReferences.Remove(unityEditor);
		
			if (DEBUG) Debug.Log($"Extracting references from Unity project took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
			return new ReferenceContainer
			{
				UnityEngine = unityEngine,
				UnityEditor = unityEditor,
				Additional = allReferences.ToList()
			};
		});
	}
	
	private static void EnsureReferences(string project, Lazy<ReferenceContainer> lazyReferenceContainer)
	{
		var started = DateTime.UtcNow;
		var fsProjectContent = File.ReadAllText(project);
		
		if (fsProjectContent.Contains("UnityEngine")) return;

		var references = lazyReferenceContainer.Value;
		var fsProjectDocument = XDocument.Parse(fsProjectContent);
		
		XElement unityMainReferences = new XElement(Xmlns + "ItemGroup");
		unityMainReferences.Add(new XElement(Xmlns + "Reference", new XAttribute("Include", references.UnityEngine.Include),
			new XElement(Xmlns + "HintPath", references.UnityEngine.HintPath)));
		unityMainReferences.Add(new XElement(Xmlns + "Reference", new XAttribute("Include", references.UnityEditor.Include),
			new XElement(Xmlns + "HintPath", references.UnityEditor.HintPath)));

		XElement unityAdditionalReferences = new XElement(Xmlns + "ItemGroup");
		foreach (var reference in references.Additional)
		{
			unityAdditionalReferences.Add(new XElement(Xmlns + "Reference", new XAttribute("Include", reference.Include),
				new XElement(Xmlns + "HintPath", reference.HintPath)));
		}

		fsProjectDocument.Root.Add(unityMainReferences);
		fsProjectDocument.Root.Add(unityAdditionalReferences);
		fsProjectDocument.Save(project);
		
		if (DEBUG) Debug.Log($"Adding references to '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
	}

	
	private static void Compile(string unityRoot, string project)
	{
		var projectDir = Path.GetDirectoryName(project);
		var projectBuildDir = Path.Combine(projectDir, "build");
		
		Directory.CreateDirectory(projectBuildDir);
		var unityAssetsPath = Path.Combine(unityRoot, "Assets");
		var projectDllFilename = Path.GetFileNameWithoutExtension(project) + ".dll";
		var projectDllBuildPath = Path.Combine(projectBuildDir, projectDllFilename);
		var projectDllAssetPath = Path.Combine(unityAssetsPath, projectDllFilename);
		var fsCoreDll = "FSharp.Core.dll";

		// Check if a recompilation is needed
		var fsFiles = Directory.EnumerateFiles(projectDir, "*.fs", SearchOption.AllDirectories);	
		var lastProjectWriteTimeUtc = File.GetLastWriteTimeUtc(projectDllAssetPath);
		var filesChanged = fsFiles.Any(file => File.GetLastWriteTimeUtc(file) > lastProjectWriteTimeUtc);
		
		if (!File.Exists(projectDllAssetPath) || filesChanged)
		{
			var started = DateTime.UtcNow;
			if (_useDotnet)
			{
				if (DEBUG) Debug.Log($"Compiling '{Path.GetFileNameWithoutExtension(project)}' using dotnet");

				var startInfo = new ProcessStartInfo("dotnet")
				{
					WindowStyle = ProcessWindowStyle.Hidden, 
					Arguments = $"build \"{project}\" --no-dependencies --no-restore --output \"{projectBuildDir}\""
				};
				Process.Start(startInfo)?.WaitForExit();
			}
			else
			{
				if (DEBUG) Debug.Log($"Compiling '{Path.GetFileNameWithoutExtension(project)}' using msbuild");
				var startInfo = new ProcessStartInfo("msbuild")
				{
					WindowStyle = ProcessWindowStyle.Hidden, 
					Arguments = $"\"{projectDir}\" -p:OutputPath=\"{projectBuildDir}\" -verbosity:quiet -maxcpucount"
				};
				Process.Start(startInfo)?.WaitForExit();
			}
			if (DEBUG) Debug.Log($"Compilation of '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
			
			// Copy needed dll .files
			started = DateTime.UtcNow;
			File.Copy(projectDllBuildPath,projectDllAssetPath, true);
			
			if (!File.Exists(Path.Combine(unityAssetsPath, fsCoreDll)))
			{
				File.Copy(Path.Combine(projectBuildDir, fsCoreDll), Path.Combine(unityAssetsPath, fsCoreDll));
			}
			
			if (DEBUG) Debug.Log($"Copying files from '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
		}
		else
		{
			Debug.Log($"The F# project '{Path.GetFileNameWithoutExtension(project)}' is already up-to-date");
		}
	}
}