# GrXviewer

Windows real-time screen / window viewer built with **.NET 8**, **WinForms**, and **Direct3D 11** (Vortice). It captures the desktop or selected programs, presents through a GPU swap chain, and applies optional upscaling, frame generation blend, motion stabilizer, and folder-based HLSL effects — with **DLSS 5 via ReShade** when you keep the ReShade / RenoDX stack in `dist\`.

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build)
- DirectX 11–capable GPU (integrated or discrete)

## Run

Published build:

```text
dist\GrXviewer.exe
```

Or publish from the repo root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish.ps1
```

That builds a self-contained `win-x64` app into `dist\` (and keeps any ReShade / addon files you already placed there).

## Build (dev)

```powershell
dotnet build .\src\GrXviewer.csproj -c Release
```

## Features

| Area | What you get |
|------|----------------|
| Capture | Full screen or selected programs; CPU (`BitBlt`) or GPU (Desktop Duplication) |
| Present | D3D11 flip / tearing when available |
| Preview | FPS target, resolution scale, **foveated** (sharp center / soft edges), frame generation, upscaling, motion stabilizer |
| Effects | Overlay panel + `.hlsl` shaders from the `shaders\` folder |
| ReShade | Optional **DLSS 5** (RenoDX / ReShade addons) beside the exe in `dist\` |
| View | Always on top, click-through, fullscreen glass (borderless) |
| Presets | Hardware detect; **Laptop preset** (GPU capture, stabilizer off, 70% res) |

Settings are saved next to the exe as `GrXviewer.settings.json`. Screenshots go under `captures\`.

## Shortcuts

These work globally while GrXviewer is running (including when another app is focused), unless a higher-privilege process blocks low-level hooks:

| Key | Action |
|-----|--------|
| **¬** (UK key left of `1`) | Toggle effects overlay |
| **F8** | Show / hide menu (exits glass if needed) |
| **F11** | Toggle borderless fullscreen |
| **Esc** | Close overlay / exit glass / show menu |
| **F5** | Refresh program list |
| **Ctrl+S** | Save screenshot |

## Menus (quick map)

- **File** — Pause, screenshot, overlay, hide menu, exit  
- **Capture** — Full screen vs programs, display, program list  
- **Preview** — FPS, resolution, frame gen, upscaling, stabilizer  
- **Graphics** — Detect hardware, **Laptop preset**, CPU/GPU capture, present GPU  
- **View** — Topmost, click-through, FPS HUD, overlay, glass  

## Custom shaders

Put `.hlsl` files in:

```text
shaders\
```

They show up in the ¬ overlay. Example files ship in that folder. Reload is watched while the app runs.

## Laptop tips

1. **Graphics → Laptop preset (faster capture)**  
2. Prefer **Capture on GPU** and the GPU that drives your display  
3. Lower **Resolution** if capture FPS is low  
4. Turn **Motion stabilizer** off for more headroom  
5. Use a high-performance power plan when plugged in  

## Notes

- Present rate can still be limited by DWM / display refresh in a windowed WinForms host; capture rate is separate (see FPS HUD: `out` vs `cap`).  
- Optional ReShade / `dxgi.dll` files in `dist\` are left in place by `publish.ps1`; the app prefers System32 DXGI/D3D11 so a local proxy does not hook Present.  
- If shared GPU textures fail (common on some Intel / hybrid setups), the viewer falls back to CPU readback / GDI so you do not stay on a black frame.

## Project layout

```text
src\           C# app (MainForm, D3D present, GPU/CPU capture, overlay)
shaders\       User / example HLSL (copied into dist\shaders on publish)
publish.ps1    Release publish → dist\
OrganizeOutput.ps1  Tidies publish staging before copy to dist\
dist\          Local runnable output (not in git — rebuild with publish.ps1)
```

## Optional: ReShade / DLSS 5

NVIDIA / ReShade / RenoDX binaries are **not** shipped in this repo (large + third-party). After `publish.ps1`, drop your ReShade `dxgi.dll`, addons, and DLSS files into `dist\` yourself; publish leaves those files alone.

## License

Use and modify as you like for personal / project use unless you add a separate license file.
