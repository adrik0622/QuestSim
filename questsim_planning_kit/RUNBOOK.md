# QuestSim runbook

Working directory: `/home/adrikingston/Projects/QuestSim`. Logs live in
`.artifacts/logs/`; generated caches stay in `.local/`. No packages added globally.

## Verified here — 2026-10-25

| Operation | Command / result |
| --- | --- |
| Versions | `dotnet --info`: SDK 10.0.110, MSBuild 18.0.11, runtime 10.0.10, Manjaro linux-x64; exact SDK in global.json |
| Offline build + checks | `bash scripts/check.sh`: Release build, 0 warnings/errors, seven checks pass |
| Initial restore failure | `restore-first-failure.log`: unused transitive Microsoft.AspNetCore.App.Ref requested but not installed |
| Restore diagnosis | DisableTransitiveFrameworkReferenceDownloads=true avoids unused pack download; no ASP.NET dependency added |
| Godot build attempt | Command below fails MSB4236, bundled Godot.NET.Sdk unavailable; godot-build.log |
| Desktop | DISPLAY=:0, X11; xdotool/xwininfo/Pillow cannot connect; socket connect returns EPERM |
| Downloads | `curl -I -L --max-time 10 https://api.github.com/repos/godotengine/godot/releases/latest` fails DNS, exit 6 |
| Git | `git status --short` fails: empty read-only .git is not a repository |

`check.sh` uses a local empty NuGet feed because these two projects have no package
dependencies. It disables compiler/build servers for socket-restricted execution.
The checks prove fixture behavior and the local C# toolchain only; no game launch,
network, persistence, collision or performance claim follows.

## Godot prerequisite and blocked command

Scaffold chooses Godot **4.6.2 .NET Linux x86_64** as the reproducible candidate,
not an established compatible runtime result. Keep GodotSharp alongside its binary.
Official [archive/download](https://godotengine.org/download/archive/4.6.2-stable/)
and [C# prerequisites](https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/c_sharp_basics.html)
provide the engine and separate SDK requirement. .NET 10 is installed; its
combination with this Godot build still needs an actual launch test.

A normal environment with download access must supply the extracted distribution:
`.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64` and
`.tools/godot/GodotSharp/Tools/nupkgs/`. Check archive integrity against the official
release checksum before extraction. No download/install succeeded in this sandbox.
`experiments/presentation/NuGet.Config` points only to those bundled packages.

Actual failed command (after supplying the distribution, rerun unchanged):

```bash
export DOTNET_CLI_HOME="$PWD/.local/dotnet"
export NUGET_PACKAGES="$PWD/.local/nuget"
dotnet build experiments/presentation/QuestSim.Presentation.csproj -c Debug -p:DisableTransitiveFrameworkReferenceDownloads=true -p:UseSharedCompilation=false --disable-build-servers > .artifacts/logs/godot-build.log 2>&1
```

Following commands are **not run**, pending that prerequisite; they are the next
launch checks, not verified commands:

```bash
.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --version
.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --headless --path experiments/presentation --quit-after 2
.tools/godot/Godot_v4.6.2-stable_mono_linux.x86_64 --path experiments/presentation
```

Headless smoke must print `QUESTSIM_BOOTSTRAP_READY actors=4` without errors.
This is a launch scaffold, not a CPU authoritative server. Window title should be
`QuestSim Presentation Lab`. A focused Space press should increment visible text
and log `QUESTSIM_PROBE_SPACE 1`. This behavior is unverified until actually run.

## Desktop discovery and exact missing capability

Read-only discovery found sibling `../pokeys-100-days-gone/scripts/desktop_control.py`.
It launches/tracks Project Zomboid specifically and writes sibling runtime files;
do not invoke it for QuestSim. No sibling file was modified. Relevant reusable
pattern: track launched child PID, locate window by PID and expected title,
activate and verify focus, capture screenshot, send bounded input with finally
key release, then inspect result. Never send keys to an unverified active window.

Python 3.14, tkinter/Tk 8.6, Pillow, xdotool, xprop/xwininfo, ImageMagick `import`
and Xvfb are installed. Their presence does not grant desktop access. Both
`/tmp/.X11-unix/X0` and the abstract X socket reject connect with EPERM in this
sandbox; `prerequisites.log` preserves failures. No screenshot was captured.
Xvfb would not prove access to the user's desktop and was not substituted.

Required capability: a permitted connection to the desktop's X server. A normal
terminal process could potentially host the sibling-style foreground file-queue
bridge entirely in QuestSim, but no bridge was built or tested: engine and direct
access are absent, and existing tools already identify the permission boundary.
Do not attempt privilege escalation or claim a bridge bypass was verified.

## Checkpoints and later checks

Original planning inputs: `.artifacts/checkpoints/000-original-planning.tar.gz`.
Current source/docs: `.artifacts/checkpoints/001-bootstrap.tar.gz`, with
`001-bootstrap.sha256` and a per-file manifest alongside it. These replace Git
checkpoints only while .git remains unavailable; generated caches are excluded.

CPU authority, two independent clients, reconnect/persistence, paused empty-world
clock, desktop exports and other target machines: **not implemented/tested**.
Run these only at their milestone. See PRESENTATION_EXPERIMENT.md for the bounded
visual comparison before selecting a pipeline.
