# Clipboard Suite

This repository contains:
- **Clipboard Blazor App (.NET 9)**
  A web UI to browse, search, import, and export clipboard history.

- **Clipboard Recorder (C++)**
  A console app that logs clipboard text and images to a SQLite database.

- **Clipboard Exporter (.NET 9 CLI)**
  Creates signed export ZIPs containing a database, images, and manifest.

- **Tests**
  - ClipManagerTests (NUnit, in-memory DB)
  - DbClipTests (GoogleTest)

## Features
- Clipboard capture: text + image paths
- Blazor viewer with search, pagination, and toasts
- Import/export with ZIP archives
- Manifest with SHA-256 hashmaps
- Optional merge of imported databases

## Solution Layout
ClipManager
DbClip
DbClipExporter
Tests/ClipManagerTests
Tests/DbClipTests

## Building
- Requires **.NET 9 SDK**
- Requires **C++20 toolchain**

## Building
```console
REM This displays the path to MSBuild.exe
"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe

git clone https://github.com/scriptar/ClipManager.git
cd ClipManager
REM Use the path MSBuild.exe from the command above...
"C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe" ClipManager.sln
```

## Running DbClip (saves clipboard entries to clipboard-history.db SQLite database)
```console
cd ClipManager\x64\Debug
DbClip.exe
```

## Running ClipManager (Blazor viewer/importer/exporter)
```console
REM For the Blazor App...
cd ClipManager\ClipManager
dotnet run
```
Then navigate to: <http://localhost:5257>

## License
MIT