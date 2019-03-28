# Unity F# integration
### _Make Unity fun<sup>ctional</sup> again!_
- Requires [.NET Core SDK](https://dotnet.microsoft.com/download)
- Download the [Unity package here](https://github.com/sppt-2k19/unity-fsharp-integration/raw/master/unity-fsharp-integration.unitypackage)

Installing this Unity package adds an F# menu to the Unity editor:
- `Compile F#` _detects F# projects in the current Unity project folder and compiles them using `dotnet`_ <kbd>F6</kbd>
- `Create F# project` _creates an F# project using `dotnet` and adds all Unity references_
- `Open F# project in default editor` _opens the F# project with the current default application for .fsproj files_
- `Include reference to C# project` _toggles the inclusion of a reference to the dll-file created by the Unity C# project_
- `Include additional references` _toggles the inclusion of references to additional Unity modules, besides `UnityEngine` and `UnityEditor`_
- `Compile in release mode` _toggles building in release mode instead of debug_
- `Show debug information` _toggles printing of debug information during compilation_

After creating an F# project through the extension, use the `Compile F#` button to compile the project and copy the resulting .dll into the `Assets/`-folder, so Unity can index it.

You can also compile from the IDE you are using to edit the F# files, as long as the dll-file is put in `bin/Debug` or `bin/Release`. 
_Just remember to click `Compile F#` to have the extension copy over the needed file._
