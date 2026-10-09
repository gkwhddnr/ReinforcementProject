# UE 5.8.3 local build

Open the editor with `OpenEditor.cmd` and build with `BuildEditor.cmd`.
Close the editor before running a standalone build.

This machine uses the directory junction `C:\UnrealProjects\Rein_Project`
to access this project without Korean characters in compiler paths.
The original project has not been moved or copied; both paths access the same files.
Use the junction path when generating IDE project files or opening the `.uproject`.

The original build failed with MSVC error C1083 while creating a shared PCH.
Compiler response files contained UTF-8 paths without a BOM, and the compiler
misinterpreted the Korean user-directory name. Disabling UBA did not resolve it.
The old `Intermediate` directory was preserved as `Intermediate.before-path-fix-*`
before regenerating build files through the ASCII-only path.

The launchers assume the engine is installed at `C:\Program Files\Epic Games\UE_5.8`.
If moving to another computer, update these local paths as necessary.
