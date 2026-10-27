# Building
1. Install the .NET SDK. Edit `RailroaderDir` in the .csproj (or pass `/p:RailroaderDir="D:\...\Railroader"`).
2. `dotnet build -c Release` builds and copies into `Mods\AutoEngineerBrakeFix`.
3. `dotnet build -c Release /p:Package=true /p:ReleaseVersion=1.12.0` also writes `dist\AutoEngineerBrakeFix-1.12.0.zip`
   (contains the `AutoEngineerBrakeFix` folder with the DLL and Info.json; unzip into `Railroader\Mods`).
Keep `Version` in Info.json in step with `ReleaseVersion`. Set `Author` first.
