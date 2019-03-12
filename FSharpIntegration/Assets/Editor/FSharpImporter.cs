using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEditor;
using Debug = UnityEngine.Debug;


public class FSharpImporter : AssetPostprocessor
{
	private const string MenuItemRecompile = "F#/Compile F#";
	private const string MenuItemAutoCompile = "F#/Enable Auto-compile";
	private const string MenuItemUseDotnet = "F#/Use dotnet compiler";
	private const string MenuItemIsDebug = "F#/Show debug information";
	private const string MenuItemCreateFSharpProject = "F#/Create F# project";
	
	private static bool _compiling = false;
	private static bool _autoRecompile = EditorPrefs.GetBool(MenuItemAutoCompile, false);
	private static bool _useDotnet = EditorPrefs.GetBool(MenuItemUseDotnet, true);
	private static bool _isDebug = EditorPrefs.GetBool(MenuItemIsDebug, true);
	
//	private static readonly XNamespace Xmlns = "http://schemas.microsoft.com/developer/msbuild/2003";
	private static readonly Regex MatchReferences =
		new Regex("<Reference Include=\"([^\"]+)\">\\s*<HintPath>([^<]+)<\\/HintPath>\\s*<\\/Reference>", RegexOptions.Compiled);

	private static bool _dotnetAvailable;
	private static bool _msbuildAvailable;

	public FSharpImporter()
	{
		_dotnetAvailable = CanExecuteCmd("dotnet", "--version");
		_msbuildAvailable = CanExecuteCmd("msbuild", "/version");
		if (!_dotnetAvailable && !_msbuildAvailable)
		{
			Debug.Log("No build tools found :(\nRequires 'dotnet' or 'msbuild' to be installed and available in the terminal");
		}
		Menu.SetChecked(MenuItemAutoCompile, EditorPrefs.GetBool(MenuItemAutoCompile, false));
		Menu.SetChecked(MenuItemUseDotnet, EditorPrefs.GetBool(MenuItemUseDotnet, _dotnetAvailable));
		Menu.SetChecked(MenuItemIsDebug, EditorPrefs.GetBool(MenuItemIsDebug, true));
	}

	static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
	{
		if (!_autoRecompile) return;
		if (_isDebug) Debug.Log ("Starting automatic recompilation");
		InvokeCompiler();
	}
	
	[MenuItem(MenuItemRecompile, false, 1)]
	public static void InvokeCompiler()
	{
		if (_useDotnet && !_dotnetAvailable || !_useDotnet && !_msbuildAvailable)
		{
			Debug.Log($"Compiler is not available {(_useDotnet ? "dotnet" : "msbuild")}");
			return;
		}
		if (!_dotnetAvailable && !_msbuildAvailable)
		{
			Debug.Log("No compilers available (dotnet, msbuild)");	
			return;
		}
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
	public static bool IsReadyForCompilation() =>
		!_compiling && (_useDotnet && _dotnetAvailable) || (!_useDotnet && _msbuildAvailable);
	
	[MenuItem(MenuItemUseDotnet, false, 51)]
	public static void ToggleDotnet()
	{
		_useDotnet = !_useDotnet;
		Menu.SetChecked(MenuItemUseDotnet, _useDotnet);
		EditorPrefs.SetBool(MenuItemUseDotnet, _useDotnet);
	}
	
	[MenuItem(MenuItemUseDotnet, true, 51)]
	public static bool IsDotnetAvailable() => _dotnetAvailable;
	
	
	[MenuItem(MenuItemAutoCompile, false, 52)]
	public static void ToggleAutoCompile()
	{
		_autoRecompile = !_autoRecompile;
		Menu.SetChecked(MenuItemAutoCompile, _autoRecompile);
		EditorPrefs.SetBool(MenuItemAutoCompile, _autoRecompile);
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
		
		if (fsProjectContent.Contains("UnityEngine")) return;

		var references = lazyReferenceContainer.Value;
		var fsProjectDocument = XDocument.Parse(fsProjectContent);
		
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
		var fsCoreDll = "FSharp.Core.dll";

		// Check if a recompilation is needed
		var fsFiles = Directory.EnumerateFiles(projectDir, "*.fs", SearchOption.AllDirectories);	
		var lastProjectWriteTimeUtc = File.GetLastWriteTimeUtc(projectDllAssetPath);
		var filesChanged = fsFiles.Any(file => File.GetLastWriteTimeUtc(file) > lastProjectWriteTimeUtc);
		
		if (!File.Exists(projectDllAssetPath) || filesChanged)
		{
			var started = DateTime.UtcNow;
			var success = (false, "");
			if (_useDotnet)
			{
				if (_isDebug) Debug.Log($"Compiling '{Path.GetFileNameWithoutExtension(project)}' using dotnet");
				success = ExecuteCmd("dotnet", $"build \"{project}\" --no-dependencies --no-restore --verbosity quiet --output \"{projectBuildDir}\"");
			}
			else
			{
				if (_isDebug) Debug.Log($"Compiling '{Path.GetFileNameWithoutExtension(project)}' using msbuild");
				success = ExecuteCmd("msbuild", $"\"{projectDir}\" -p:OutputPath=\"{projectBuildDir}\" -verbosity:quiet -maxcpucount");
			}

			if (!success.Item1)
			{
				Debug.LogError($"Compilation using {(_useDotnet ? "dotnet" : "msbuild")} failed!");
				Debug.LogError(success.Item2);
				return;
			}
			
			if (_isDebug) Debug.Log($"Compilation of '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
			
			// Copy needed dll .files
			started = DateTime.UtcNow;
			File.Copy(projectDllBuildPath,projectDllAssetPath, true);
			
			if (!File.Exists(Path.Combine(unityAssetsPath, fsCoreDll)))
			{
				File.Copy(Path.Combine(projectBuildDir, fsCoreDll), Path.Combine(unityAssetsPath, fsCoreDll));
			}
			
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
		var outputBuilder = new StringBuilder();
		var proc = Process.Start(new ProcessStartInfo(cmd)
		{
			WindowStyle = ProcessWindowStyle.Hidden, 
			Arguments = args
		});
		proc.OutputDataReceived += (s, e) => outputBuilder.AppendLine(e.Data);
		proc.ErrorDataReceived += (s, e) => outputBuilder.AppendLine(e.Data);
		proc?.WaitForExit();
		return (proc.ExitCode == 0, outputBuilder.ToString());
	}
}