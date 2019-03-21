# Unity F# integration
### _Make Unity fun<sup>ctional</sup> again!_
- Requires [.NET Core SDK](https://dotnet.microsoft.com/download)
- Download the [Unity package here](https://github.com/sppt-2k19/unity-fsharp-integration/raw/master/unity-fsharp-integration.unitypackage)

Installing this Unity package adds an F# menu to the Unity editor:
- __Compile F#__ _detects F# projects in the current Unity project folder and compiles them using `dotnet`_
- __Show debug information__ _toggles printing of debug information during compilation_
- __Create F# project__ _creates an F# project using `dotnet` and adds all Unity references_

After creating a project through the extension, use the `Compile F#` button to compile the project and copy the resulting .dll into the `Assets/`-folder, so Unity can index it
