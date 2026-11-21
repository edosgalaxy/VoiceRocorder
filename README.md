# VoiceRocorder

A Windows 11-style voice recorder for Windows 11 x64.

## Features

- Modern dark interface inspired by Windows 11 Voice Recorder.
- Select an active Windows recording endpoint.
- Capture audio through WASAPI shared mode (instead of legacy `waveInOpen`).
- Create sequential takes as separate tracks in one project.
- Play original or reversed audio per track.
- Reverse whole audio frames, preserving channel/sample frame alignment.
- Export selected take as WAV.
- Save/reopen project files with audio copied to a companion `_Audio` folder.
- Self-contained Windows x64 publish.

## Build with GitHub Actions

1. Upload the contents of this ZIP to the root of `edosgalaxy/VoiceRocorder`, replacing the old files. Keep the `.github/workflows/build-windows.yml` path.
2. Open **Actions** in the repository.
3. Open **Build VoiceRocorder (Windows x64)**.
4. Click **Run workflow** (or wait for the push-triggered build).
5. Open the completed run and download `VoiceRocorder-Windows-x64` under Artifacts.
6. Extract the ZIP and run `VoiceRocorder.exe`.

## Local build

Requires the .NET 8 SDK on Windows:

```powershell
dotnet restore .\VoiceRocorder.sln
dotnet publish .\VoiceRocorder\VoiceRocorder.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Notes

WASAPI shared-mode capture uses the selected endpoint's mix format. The first launch and device availability still depend on Windows audio services, driver state, and microphone privacy permissions.
