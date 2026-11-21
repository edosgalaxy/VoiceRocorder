# VoiceRocorder

Windows 11 x64 voice recorder with multiple sequential tracks, per-track reverse playback, WAV export, and JSON project save/load.

## Build automatically with GitHub Actions

1. Upload the contents of this folder to the root of `edosgalaxy/VoiceRocorder` (keep the `.github` folder).
2. Open the repository's **Actions** tab.
3. Select **Build VoiceRocorder (Windows x64)** and click **Run workflow**.
4. When the workflow finishes, open its run and download the `VoiceRocorder-Windows-x64` artifact.

The workflow publishes a self-contained Windows x64 app. The first build restores NAudio from NuGet, so GitHub Actions needs network access.

## Local build

Install the .NET 8 SDK, then run:

```powershell
dotnet restore .\VoiceRocorder.sln
dotnet publish .\VoiceRocorder\VoiceRocorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The executable is created under `VoiceRocorder\bin\Release\net8.0-windows\win-x64\publish\`.

## Features

- Select an available microphone.
- Record sequential takes as separate tracks in one project.
- Play each track normally or reversed.
- Switch back to the original without modifying it.
- Export the selected version as WAV.
- Save and reopen a project; audio files are copied beside the project in a `_Audio` folder.

Audio is recorded as 44.1 kHz, 16-bit, mono PCM WAV.
