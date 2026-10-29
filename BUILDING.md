# Building
1. Install the .NET SDK. Edit `RailroaderDir` in the .csproj (or pass `/p:RailroaderDir="D:\...\Railroader"`).
2. `dotnet build -c Release` builds and copies into `Documents\Railroader\Mods\AutoEngineerBrakeFix` (override with `/p:ModsDir=...`).
3. `dotnet build -c Release /p:Package=true /p:ReleaseVersion=2.2.3` also writes `dist\AutoEngineerBrakeFix-2.2.3.zip`
   (contains the `AutoEngineerBrakeFix` folder with the DLL, 0Harmony.dll and Info.json; unzip into `Documents\Railroader\Mods`).
Keep `Version` in Info.json in step with `ReleaseVersion`. Set `Author` first.
