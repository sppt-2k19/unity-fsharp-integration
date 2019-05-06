using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEditor;
using Path = System.IO.Path;
using File = System.IO.File;
using Directory = System.IO.Directory;

namespace FSharpIntegration.Editor
{
	[InitializeOnLoad]
	public static class FSharpImporter
	{
		private const string MenuItemRecompile = "F#/Compile F# _F6";
		private const string MenuItemCreateFSharpProject = "F#/Create F# project";
		private const string MenuItemOpenFSharpProject = "F#/Open F# project in default editor";

		private const string MenuItemReferenceCSharpDll = "F#/Include reference to C# project";
		private const string MenuItemIncludeAdditionalReferences = "F#/Include additional references";
		private const string MenuItemReleaseBuild = "F#/Compile in release mode";
		private const string MenuItemIsDebug = "F#/Show debug information";

		private const string CSharpProject = "Assembly-CSharp.csproj";
		private const string FSharpProject = "Assembly-FSharp.fsproj";
		
		private const string Version = "1.2.5";

		private static readonly string[] IgnoredFiles = { "Assembly-FSharp.dll", "FSharp.Core.dll" };
	
		private static bool _compiling = false;
		private static readonly bool DotnetAvailable;
	
		private static bool ShowDebugInfo
		{
			get => UnityEditor.EditorPrefs.GetBool(MenuItemIsDebug, false);
			set => UnityEditor.EditorPrefs.SetBool(MenuItemIsDebug, value);
		}
		private static bool IsReleaseMode
		{
			get => UnityEditor.EditorPrefs.GetBool(MenuItemIsDebug, false);
			set => UnityEditor.EditorPrefs.SetBool(MenuItemIsDebug, value);
		}
		private static bool ReferenceCSharpDll
		{
			get => UnityEditor.EditorPrefs.GetBool(MenuItemReferenceCSharpDll, false);
			set => UnityEditor.EditorPrefs.SetBool(MenuItemReferenceCSharpDll, value);
		}
		private static bool IncludeAdditionalReferences
		{
			get => UnityEditor.EditorPrefs.GetBool(MenuItemIncludeAdditionalReferences, false);
			set => UnityEditor.EditorPrefs.SetBool(MenuItemIncludeAdditionalReferences, value);
		}

		private static void Print(string message) => UnityEngine.Debug.Log("F# : " + message);
		private static void PrintWarning(string message) => UnityEngine.Debug.LogWarning("F# : " + message);
		private static void PrintError(string message) => UnityEngine.Debug.LogError("F# : " + message);
	
		private static readonly Regex MatchReferences =
			new Regex("<Reference Include=\"([^\"]+)\">\\s*<HintPath>([^<]+)<\\/HintPath>\\s*<\\/Reference>", RegexOptions.Compiled);


		static FSharpImporter()
		{
			Print($"version {Version} loaded");
			DotnetAvailable = CanExecuteCmd("dotnet", "--version");
			if (!DotnetAvailable)
			{
				PrintWarning("dotnet was not found. Please install and ensure it is available from a terminal");
			}
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
			try
			{
				UnityEditor.Menu.SetChecked(MenuItemIsDebug, ShowDebugInfo);
				UnityEditor.Menu.SetChecked(MenuItemReleaseBuild, IsReleaseMode);
				UnityEditor.Menu.SetChecked(MenuItemReferenceCSharpDll, ReferenceCSharpDll);
				UnityEditor.Menu.SetChecked(MenuItemIncludeAdditionalReferences, IncludeAdditionalReferences);
			}
			catch (Exception) { }
		}

		private static void OnPlayModeStateChanged(PlayModeStateChange change)
		{
			if (change == PlayModeStateChange.EnteredPlayMode)
			{
				EditorApplication.ExitPlaymode();
				try
				{
					Build();
					EditorApplication.EnterPlaymode();
				} 
				catch (Exception e) {
					UnityEngine.Debug.LogException(e);
				}
			}
		}

		private static void EnsureCSharpProjectExistance()
		{
			var dir = Directory.GetCurrentDirectory();
			if (!File.Exists(Path.Combine(dir, CSharpProject))){
				UnityEditor.EditorApplication.ExecuteMenuItem("Assets/Open C# Project");
			}
		}

		[UnityEditor.MenuItem(MenuItemRecompile, false, 1)]
		public static void InvokeCompiler()
		{
			if (_compiling)
			{
				PrintWarning("already compiling...");
			}
			else
			{
				_compiling = true;
				try
				{
					Build();
				} 
				catch (Exception e) {
					UnityEngine.Debug.LogException(e);
				}
				_compiling = false;
			}
		}

		private static void Build()
		{
			var dir = Directory.GetCurrentDirectory();
			var fsProjects = Directory.EnumerateFiles(dir, "*.fsproj", SearchOption.AllDirectories);
			var releaseBuild = IsReleaseMode;
			Print($"compiling in {(releaseBuild ? "release" : "debug")} mode..");
			var references = ExtractUnityReferences(dir, releaseBuild);
			foreach (var project in fsProjects)
			{
				UpdateReferences(project, references);
				Compile(dir, project, releaseBuild);
			}
		}
		
		[UnityEditor.MenuItem(MenuItemRecompile, true, 1)]
		public static bool CanInvokeCompiler()
		{
			return DotnetAvailable && !_compiling;
		}
	
		[UnityEditor.MenuItem(MenuItemCreateFSharpProject, false, 2)]
		public static void CreateFSharpProject()
		{
			var dir = Directory.GetCurrentDirectory();
			var name = "Assembly-FSharp";
			var projectDir = Path.Combine(dir, name);
		
			Directory.CreateDirectory(projectDir);
			ExecuteCmd("dotnet", $"new classlib --language F# --name \"{name}\" --output \"{projectDir}\"");
			UpdateReferences(Path.Combine(projectDir, name + ".fsproj"), ExtractUnityReferences(dir, IsReleaseMode));
		}
	
		[UnityEditor.MenuItem(MenuItemCreateFSharpProject, true, 2)]
		public static bool CanCreateFSharpProject()
		{
			var projectDir = Path.Combine(Directory.GetCurrentDirectory(), "Assembly-FSharp");
			return DotnetAvailable && !Directory.Exists(projectDir);
		}
	
		[UnityEditor.MenuItem(MenuItemOpenFSharpProject, false, 3)]
		public static void OpenFSharpProject()
		{
			var fsProjectPath = Path.Combine(Directory.GetCurrentDirectory(), "Assembly-FSharp", FSharpProject);
			if (File.Exists(fsProjectPath))
			{
				Process.Start(fsProjectPath);
			}
			else
			{
				PrintError("cannot find the F# project to open");
			}
		}
	
		[UnityEditor.MenuItem(MenuItemOpenFSharpProject, true, 3)]
		public static bool CanOpenFSharpProject()
		{
			var fsProjectPath = Path.Combine(Directory.GetCurrentDirectory(), "Assembly-FSharp", FSharpProject);
			return File.Exists(fsProjectPath);
		}
	
		[UnityEditor.MenuItem(MenuItemReferenceCSharpDll, false, 51)]
		public static void ToggleReferenceCSharpDll()
		{
			ReferenceCSharpDll = !ReferenceCSharpDll;
			UnityEditor.Menu.SetChecked(MenuItemReferenceCSharpDll, ReferenceCSharpDll);
		}
	
		[UnityEditor.MenuItem(MenuItemIncludeAdditionalReferences, false, 52)]
		public static void ToggleIncludeAdditionalReferences()
		{
			IncludeAdditionalReferences = !IncludeAdditionalReferences;
			UnityEditor.Menu.SetChecked(MenuItemIncludeAdditionalReferences, IncludeAdditionalReferences);
		}
	
		[UnityEditor.MenuItem(MenuItemReleaseBuild, false, 53)]
		public static void ToggleReleaseBuild()
		{
			IsReleaseMode = !IsReleaseMode;
			UnityEditor.Menu.SetChecked(MenuItemReleaseBuild, IsReleaseMode);
		}
	
		[UnityEditor.MenuItem(MenuItemIsDebug, false, 54)]
		public static void ToggleIsDebug()
		{
			ShowDebugInfo = !ShowDebugInfo;
			UnityEditor.Menu.SetChecked(MenuItemIsDebug, ShowDebugInfo);
		}
		
		private static UnityReferenceContainer ExtractUnityReferences(string dir, bool releaseBuild)
		{
			var started = DateTime.UtcNow;
			var unityCsProject = Path.Combine(dir, CSharpProject);
			if (!File.Exists(unityCsProject))
			{
				throw new FileNotFoundException("No Unity projects to copy references from found. Please add a C# script, open it, and try again");
			}
			
			var allReferences = new HashSet<UnityReference>();

			var csProjectContent = File.ReadAllText(unityCsProject);
			var matches = MatchReferences.Matches(csProjectContent);
			foreach (Match match in matches)
			{
				var include = match.Groups[1].Value;
				var hintPath = match.Groups[2].Value;
				if (IgnoredFiles.Any(file => hintPath.EndsWith(file))) continue;
				allReferences.Add(new UnityReference(include, hintPath));
			}


			UnityReference csharpDllUnityReference = null;
			if (ReferenceCSharpDll)
			{
				var csharpLibrary = Directory.GetFiles(dir, "Assembly-CSharp.dll", SearchOption.AllDirectories);
				var mode = releaseBuild ? "Release" : "Debug";
				var properLibrary = csharpLibrary.FirstOrDefault(dll => dll.Contains(mode)) ?? csharpLibrary.FirstOrDefault();
				if (properLibrary != null)
				{
					csharpDllUnityReference = new UnityReference("Assembly-CSharp", properLibrary);
				}
			}

			var unityEngine = allReferences.FirstOrDefault(r => r.Include == "UnityEngine");
			var unityEditor = allReferences.FirstOrDefault(r => r.Include == "UnityEditor");
		
			allReferences.Remove(unityEngine);
			allReferences.Remove(unityEditor);
		
			if (ShowDebugInfo) Print($"extracting references from Unity project took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
			return new UnityReferenceContainer
			{
				UnityEngine = unityEngine,
				UnityEditor = unityEditor,
				CSharpDll = csharpDllUnityReference,
				Additional = allReferences.ToList()
			};
		}
	
		private static void UpdateReferences(string project, UnityReferenceContainer references)
		{
			var started = DateTime.UtcNow;
			var fsProjectDocument = XDocument.Load(project);
		
			// Remove existing references
			fsProjectDocument
				.Descendants("ItemGroup")
				.Where(itemGroup => itemGroup.FirstNode is XElement element && element.Name == "Reference")
				.Remove();
		
			var mainUnityReferencesGroup = new XElement("ItemGroup");
			mainUnityReferencesGroup.Add(references.UnityEngine.ToXElement());
			mainUnityReferencesGroup.Add(references.UnityEditor.ToXElement());
			fsProjectDocument.Root.Add(mainUnityReferencesGroup);

			if (ReferenceCSharpDll)
			{
				if (references.CSharpDll != null)
				{
					var csharpReferenceGroup = new XElement("ItemGroup");
					csharpReferenceGroup.Add(references.CSharpDll.ToXElement());
					fsProjectDocument.Root.Add(csharpReferenceGroup);
				}
				else
				{
					PrintError("no C# project dll was found to reference. Please create a C# script and open it, to have Unity generate a C# project");
				}
			}
		
			if (IncludeAdditionalReferences)
			{
				var additionalUnityReferencesGroup = new XElement("ItemGroup");
				foreach (var reference in references.Additional)
				{
					additionalUnityReferencesGroup.Add(reference.ToXElement());
				}
				fsProjectDocument.Root.Add(additionalUnityReferencesGroup);
			}

			fsProjectDocument.Save(project);
		
			if (ShowDebugInfo) Print($"adding references to '{Path.GetFileNameWithoutExtension(project)}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
		}
	
		private static readonly Regex FileErrorRegex = new Regex("\\.fs\\((\\d{1,7}),(\\d{1,7})\\)", RegexOptions.Compiled);
		private static readonly Regex TargetFrameworkRegex = new Regex("<TargetFramework>([^<]+)<\\/TargetFramework>", RegexOptions.Compiled);
		private static void Compile(string unityRoot, string project, bool releaseMode)
		{
			var projectName = Path.GetFileNameWithoutExtension(project);
			var projectDir = Path.GetDirectoryName(project);
			var configuration = releaseMode ? "Release" : "Debug";

			var fsProjContent = File.ReadAllText(project);
			var match = TargetFrameworkRegex.Match(fsProjContent);
			if (!match.Success)
			{
				PrintError($"could not parse the TargetFramework in {Path.GetFileName(project)}");
				return;
			}

			var targetFramework = match.Groups[1].Value;
			var projectBuildDir = Path.Combine(projectDir, "bin", configuration, targetFramework);
		
			Directory.CreateDirectory(projectBuildDir);
			var projectDllFilename = projectName + ".dll";
			var projectDllBuildPath = Path.Combine(projectBuildDir, projectDllFilename);
			var projectDllAssetPath = Path.Combine(unityRoot, "Assets", projectDllFilename);
		
			var buildFileLastModified = File.GetLastWriteTimeUtc(projectDllBuildPath);
			var assetFileLastModified = File.GetLastWriteTimeUtc(projectDllAssetPath);
			var fsFiles = Directory.EnumerateFiles(projectDir, "*.fs", SearchOption.AllDirectories);	
		
			var compileRequired = fsFiles.Any(file => File.GetLastWriteTimeUtc(file) > buildFileLastModified);
			var copyRequired = compileRequired || buildFileLastModified > assetFileLastModified;

			if (!compileRequired && !copyRequired)
			{
				Print($"<color=green>'{projectName}' is already up-to-date</color>");
				return;
			}

			DateTime started;
			if (compileRequired)
			{
				started = DateTime.UtcNow;
				var arg =
					$"build \"{project}\" --no-dependencies --configuration {configuration} --verbosity quiet --output \"{projectBuildDir}\"";
				var (success, trimmedErrors) = ExecuteCmd("dotnet", arg);
				if (!success)
				{
					var errorLines = trimmedErrors.Split('\n').Skip(3).Select(s =>
					{
						var fileMatch = FileErrorRegex.Match(s);
						if (fileMatch.Success)
						{
							var filename = Path.GetFileNameWithoutExtension(s.Substring(0, fileMatch.Index + 3)) + fileMatch.Value;
							s = filename + s.Substring(fileMatch.Index + fileMatch.Length);
						}
						var index = s.LastIndexOf('[');
						return index == -1 ? s : s.Substring(0, index - 1);
					});
					trimmedErrors = string.Join("\n", errorLines);
					PrintError($"compilation using dotnet failed:\n{trimmedErrors}");
					return;
				}
				if (ShowDebugInfo) Print($"compilation of '{projectName}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms");
			}
		
			started = DateTime.UtcNow;
			File.Copy(projectDllBuildPath, projectDllAssetPath, true);
			Print(ShowDebugInfo
				? $"<color=green>copying '{projectDllFilename}' took {DateTime.UtcNow.Subtract(started).TotalMilliseconds:F2}ms</color>"
				: $"<color=green>compilation of '{projectName}' completed</color>");
		}

		private static bool CanExecuteCmd(string cmd, string args)
		{
			if (cmd == "dotnet" && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				// Use full path on macOS because it can't handle it otherwise
				cmd = "/usr/local/share/dotnet/dotnet";
			}
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
			catch (FileNotFoundException)
			{
				return false;
			}
			catch (Exception)
			{
				return true;
			}
		}

		private static (bool, string) ExecuteCmd(string cmd, string args)
		{
			if (cmd == "dotnet" && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
			{
				// Use full path on macOS because it can't handle it otherwise
				cmd = "/usr/local/share/dotnet/dotnet";
			}
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
}
