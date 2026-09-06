# Packaging and sharing a build

How to turn the three repositories into one `.yak` file you can send to someone,
and how they install it. No Yak server involved — publishing to the package
server is a separate, later step.

## The short answer

You do **not** package three things. There is one package, and it is built from
**Rhino3D**. Core and StructuralForm compile into DLLs that land in
`Rhino3D/dist/` alongside the `.rhp` and the `.gha`, and that one folder is what
gets zipped into the `.yak`.

**Always run the build from `Rhino3D`.** Never from Core or StructuralForm —
those build a library and nothing else.

## One-time setup

### 1. Clone all three as siblings

The names matter. Both front-ends look for `..\..\..\Core\...` and
`..\..\..\StructuralForm\...` relative to their own `.csproj`, so the three
folders must sit in the same parent with exactly these names:

```
<any parent folder>\
├── Core\
├── StructuralForm\
└── Rhino3D\
```

```bash
git clone https://github.com/Otter-Logic/Core.git
git clone https://github.com/Otter-Logic/StructuralForm.git
git clone https://github.com/Otter-Logic/Rhino3D.git
```

If a sibling is missing, the build does not fail — it silently falls back to a
`PackageReference` on a published NuGet package. Which is not published yet, so
you get a restore error instead of a helpful message. Check the folder names
first when something looks wrong.

### 2. Confirm the prerequisites

| Need | Check |
|---|---|
| Rhino 8, Windows | `"C:\Program Files\Rhino 8\System\Yak.exe"` exists |
| .NET SDK | `dotnet --version` |
| PowerShell | `powershell` (5.1) is enough. `pwsh` is PowerShell 7 and may not be installed |

## Building a package

### Step 1 — Close Rhino

Not optional. Rhino holds `OtterLogic.rhp`, `OtterLogic.Grasshopper.gha` and
both DLLs open while it runs, and if you have run `link-dev.ps1` those files
*are* the ones in `dist/`. Build with Rhino open and you get a wall of MSB3027
"could not copy / file is locked" errors.

```bash
Get-Process Rhino -ErrorAction SilentlyContinue
```

Empty output means you are clear.

### Step 2 — Bump the version

Edit `<Version>` in `Rhino3D/Directory.Build.props`. `pack.ps1` reads it from
there and names the package after it.

Do this for **every build you hand to someone**. Packages are keyed by name and
version, so a second `0.1.0` looks already-installed to Yak and the recipient
silently keeps the old one.

### Step 3 — Build and pack

From the `Rhino3D` folder:

```bash
powershell -ExecutionPolicy Bypass -File build/pack.ps1
```

That builds Release, copies `build/manifest.yml` into `dist/`, and runs
`yak build`. Result:

```
dist/otterlogic-<version>-rh8_0-win.yak
```

`rh8_0` is the minimum Rhino, from the 8.0 API pin. It installs on any Rhino 8.

### Step 4 — Check what you built

```bash
powershell -Command "Expand-Archive dist\otterlogic-*.yak -DestinationPath $env:TEMP\yakcheck -Force; Get-ChildItem $env:TEMP\yakcheck"
```

All of these must be present:

| File | From |
|---|---|
| `OtterLogic.rhp` | Rhino3D |
| `OtterLogic.Grasshopper.gha` | Rhino3D |
| `OtterLogic.Core.dll` | **Core** |
| `OtterLogic.StructuralForm.dll` | **StructuralForm** |
| `OtterLogic.rui` | the toolbar |
| `otterlogic.png`, `manifest.yml` | package metadata |

A missing `Core.dll` or `StructuralForm.dll` means the sibling folders are not
where the build expects them. Go back to setup step 1.

An older `.yak` sitting in `dist/` is not a problem — `yak build` skips it
rather than nesting it.

## Sending it

Send the single `.yak` file. Nothing else — not `dist/`, not the repos.

The recipient needs **Rhino 8 on Windows, any 8.x**. Nothing else to install:
Rhino ships the .NET 7 runtime the plug-in needs.

### They install it

With **Rhino closed**:

```bash
& 'C:\Program Files\Rhino 8\System\Yak.exe' install C:\path\to\otterlogic-0.1.0-rh8_0-win.yak
```

Then start Rhino. The plug-in registers, Grasshopper picks up the `OtterLogic`
tab, and the toolbar loads itself from the package folder. No `PlugInManager`
step, no Grasshopper developer-settings step, no admin rights — it installs
under `%APPDATA%\McNeel\Rhinoceros\packages\8.0\OtterLogic\<version>\`.

To remove:

```bash
& 'C:\Program Files\Rhino 8\System\Yak.exe' uninstall OtterLogic
```

### If it arrived by email, Teams or download

Windows marks downloaded files and Rhino may then refuse to load the DLLs, with
no visible error. Have them run this before installing:

```bash
Get-ChildItem C:\path\to\otterlogic-*.yak -Recurse | Unblock-File
```

## Troubleshooting

| Symptom | Cause |
|---|---|
| `MSB3027` / `file is locked by Rhino 8` | Rhino is running. Close it. |
| `NU1101: Unable to find package OtterLogic.Core` | A sibling repo is missing or misnamed. |
| `Yak not found at ...` | Rhino 8 not installed, or not at the default path. |
| `pwsh is not recognized` | Use `powershell` instead, or install PowerShell 7. |
| Installed, but no tools in Rhino | Rhino was open during install. Restart it. |
| Recipient still sees the old version | Version was not bumped. Bump and rebuild. |

## Known issue: the package contains Debug binaries

`pack.ps1` builds `-c Release`, but Core and StructuralForm still come out as
**Debug** builds. MSBuild deliberately unsets Configuration for project
references that are not members of the solution being built, and neither
dependency is listed in `OtterLogic.slnx`. The `.rhp` and `.gha` are Release;
the two DLLs beside them are not.

Verified fix — one line in `Rhino3D/Directory.Build.props`:

```xml
<ShouldUnsetParentConfigurationAndPlatform>false</ShouldUnsetParentConfigurationAndPlatform>
```

With that set, both dependencies produce `bin/Release` and the packaged DLLs
change accordingly. It works today but is not yet applied.
