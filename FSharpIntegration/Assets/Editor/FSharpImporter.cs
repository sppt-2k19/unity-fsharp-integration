using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;


public class FSharpImporter : AssetPostprocessor
{
	public const string MenuItemRecompile = "F#/Compile F#";
	public const string MenuItemIsDebug = "F#/Show debug information";
	public const string MenuItemCreateFSharpProject = "F#/Create F# project";

	private const string Version = "1.1.5";

	private static string[] IgnoredFiles = { "Assembly-FSharp.dll", "FSharp.Core.dll" };
	
	private static bool _compiling = false;
	private static bool _isDebug = EditorPrefs.GetBool(MenuItemIsDebug, true);
	
	private static readonly Regex MatchReferences =
		new Regex("<Reference Include=\"([^\"]+)\">\\s*<HintPath>([^<]+)<\\/HintPath>\\s*<\\/Reference>", RegexOptions.Compiled);

	private static bool _dotnetAvailable;

	public FSharpImporter()
	{
		Debug.Log($" -- F# Unity-integration v. {Version} -- ");
		_dotnetAvailable = CanExecuteCmd("dotnet", "--version");
		if (!_dotnetAvailable)
		{
			Debug.Log("No build tools found :(\nRequires 'dotnet' to be installed and available in a terminal");
		}
		Menu.SetChecked(MenuItemIsDebug, EditorPrefs.GetBool(MenuItemIsDebug, true));
	}
	
	[MenuItem(MenuItemRecompile, false, 1)]
	public static void InvokeCompiler()
	{
		if (!_dotnetAvailable)
		{
			Debug.Log($"The dotnet compiler is not available");
			return;
		}

		if (_compiling)
		{
			Debug.Log("Already compiling...");
			return;
		}
		_compiling = true;
		try
		{
			var dir = Directory.GetCurrentDirectory();
			var fsProjects = Directory.EnumerateFiles(dir, "*.fsproj", SearchOption.AllDirectories);
			var references = ExtractUnityReferences(dir);
			foreach (var project in fsProjects)
			{
				EnsureReferences(project, references);
				Compile(dir, project);
			}
		} 
		catch (Exception e) {
			Debug.LogError(e);
		}
		_compiling = false;
	}
	
	[MenuItem(MenuItemRecompile, true, 1)]
	public static bool CanInvokeCompiler()
	{
		return !_compiling;
	}
	
	[MenuItem(MenuItemIsDebug, false, 53)]
	public static void ToggleIsDebug()
	{
		_isDebug = !_isDebug;
		Menu.SetChecked(MenuItemIsDebug, _isDebug);
		EditorPrefs.SetBool(MenuItemIsDebug, _isDebug);
	}

	
	[MenuItem(MenuItemCreateFSharpProject, false, 54)]
	public static void CreateFSharpProject()
	{
		var dir = Directory.GetCurrentDirectory();
		var name = "Assembly-FSharp";
		var projectDir = Path.Combine(dir, name);
		
		Directory.CreateDirectory(projectDir);
		ExecuteCmd("dotnet", $"new classlib --language F# --name \"{name}\" --output \"{projectDir}\"");
		EnsureReferences(Path.Combine(projectDir, name + ".fsproj"), ExtractUnityReferences(dir));
	}
	
	[MenuItem(MenuItemCreateFSharpProject, true, 54)]
	public static bool CanCreateFSharpProject()
	{
		var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "Assembly-FSharp");
		return _dotnetAvailable && !Directory.Exists(projectDir);
	}
	
	private static Lazy<ReferenceContainer> ExtractUnityReferences(string dir)
	{
		return new Lazy<ReferenceContainer>(() =>
		{
			var started = DateTime.UtcNow;
			var unityCsProjects = Directory.GetFiles(dir, "*.csproj", SearchOption.TopDirectoryOnly);
			if (!unityCsProjects.Any()) throw new FileNotFoundException("No Unity projects to copy references from found. Please add a C# script, open it, and try again");
			
			var allReferences = new HashSet<Reference>();
			var csDlls = Directory.GetFiles(dir, "Assembly-CSharp.dll", SearchOption.AllDirectories);
			var properDll = csDlls.FirstOrDefault(dll => dll.Contains("Release")) ?? csDlls.FirstOrDefault();

			if (properDll != null) allReferences.Add(new Reference("Assembly-CSharp", properDll));
			
			foreach (var project in unityCsProjects)
			{
				var csProjectContent = File.ReadAllText(project);
				var matches = MatchReferences.Matches(csProjectContent);
				foreach (Match match in matches)
				{
					var include = match.Groups[1].Value;
					var hintPath = match.Groups[2].Value;
					if (IgnoredFiles.Any(file => hintPath.EndsWith(file))) continue;
					allReferences.Add(new Reference(include, hintPath));
				}
			}

			var unityEngine = allReferences.FirstOrDefault(r => r.Include == "UnityEngine");
			var unityEditor = allReferences.FirstOrDefault(r => r.Include == "UnityEditor");
		
			allReferences.Remove(unityEngine);
			allReferences.Remove(unityEditor);
		
			if (_isDebug) Debug.Log($"Extracting references from Unity project took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
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
		
		var references = lazyReferenceContainer.Value;
		var fsProjectDocument = XDocument.Parse(fsProjectContent);
		
		// Remove existing references
		fsProjectDocument
			.Descendants("ItemGroup")
			.Where(ig => ig.FirstNode is XElement el && el.Name == "Reference")
			.Remove();
		
		var unityMainReferences = new XElement("ItemGroup");
		unityMainReferences.Add(new XElement("Reference", new XAttribute("Include", references.UnityEngine.Include),
			new XElement("HintPath", references.UnityEngine.HintPath)));
		unityMainReferences.Add(new XElement("Reference", new XAttribute("Include", references.UnityEditor.Include),
			new XElement("HintPath", references.UnityEditor.HintPath)));

		var unityAdditionalReferences = new XElement("ItemGroup");
		foreach (var reference in references.Additional)
		{
			unityAdditionalReferences.Add(new XElement("Reference", new XAttribute("Include", reference.Include),
				new XElement("HintPath", reference.HintPath)));
		}

		fsProjectDocument.Root.Add(unityMainReferences);
		fsProjectDocument.Root.Add(unityAdditionalReferences);
		fsProjectDocument.Save(project);
		
		if (_isDebug) Debug.Log($"Adding references to '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
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
		
		// Check if a recompilation is needed
		var fsFiles = Directory.EnumerateFiles(projectDir, "*.fs", SearchOption.AllDirectories);	
		var lastProjectWriteTimeUtc = File.GetLastWriteTimeUtc(projectDllAssetPath);
		var filesChanged = fsFiles.Any(file => File.GetLastWriteTimeUtc(file) > lastProjectWriteTimeUtc);
		
		if (!File.Exists(projectDllAssetPath) || filesChanged)
		{
			var started = DateTime.UtcNow;
			var success = (false, "");
			
			if (_isDebug) Debug.Log($"Compiling '{Path.GetFileNameWithoutExtension(project)}' using dotnet");
			success = ExecuteCmd("dotnet", $"build \"{project}\" --no-dependencies --no-restore --verbosity quiet --output \"{projectBuildDir}\"");
			
			if (!success.Item1)
			{
				Debug.LogError($"Compilation using dotnet failed!\n{success.Item2}");
				return;
			}
			
			if (_isDebug) Debug.Log($"Compilation of '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
			
			started = DateTime.UtcNow;
			File.Copy(projectDllBuildPath,projectDllAssetPath, true);
			
			if (_isDebug) Debug.Log($"Copying files from '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
		}
		else
		{
			Debug.Log($"The F# project '{Path.GetFileNameWithoutExtension(project)}' is already up-to-date");
		}
	}

	private static bool CanExecuteCmd(string cmd, string args)
	{
		try
		{
			var startInfo = new ProcessStartInfo(cmd)
			{
				WindowStyle = ProcessWindowStyle.Hidden, 
				Arguments = args
			};
			Process.Start(startInfo)?.WaitForExit();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static (bool, string) ExecuteCmd(string cmd, string args)
	{
		var proc = Process.Start(new ProcessStartInfo(cmd)
		{
			WindowStyle = ProcessWindowStyle.Hidden, 
			Arguments = args,
			UseShellExecute = false,
			RedirectStandardOutput = true
		});
		var output = proc?.StandardOutput.ReadToEnd();
		proc?.WaitForExit();
		return (proc?.ExitCode == 0, output);
	}
}