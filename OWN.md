# OptiScaler Own

This branch is based on the official `optiscaler/OptiScaler` master branch at
`6ded74bfa4fbb932fda7184c1263fc45380d5b1d` (2026-09-23). It carries
separately attributed Aurora-derived experimental MFG-on-Ada and DLSS Neural
Rendering work, plus a community manager. It is not an official OptiScaler or
NVIDIA release.

## Source updates

`upstream` points to the official OptiScaler repository. Update with `git fetch
upstream` and merge or rebase `upstream/master` into this feature branch after
reviewing conflicts. `aurora-source` is reference-only; it is not the update
base. Submodules follow the commits pinned by the official parent repository;
update with `git submodule update --init --recursive` after an upstream merge.

## Binary dependencies

No NVIDIA, Nukem, Arturs, AMD or Intel runtime DLL is committed to this branch.
Build artifacts and runtime binaries must be reviewed and staged separately.
The verified local Aurora package used DLSSG 310.9.0.0, DLSSNR 310.8.0.0 and
Streamline `sl.dlss_g.dll` 2.14.0.0. These are reference versions, not bundled
components or a promise of compatibility. MFG's in-memory signatures are
version-specific; fail closed and re-test on any runtime change.

| Component | Source | Policy |
| --- | --- | --- |
| Base OptiScaler | https://github.com/optiscaler/OptiScaler | Source merge and build |
| FidelityFX | https://github.com/GPUOpen-LibrariesAndSDKs/FidelityFX-SDK | Pinned submodules |
| XeSS | https://github.com/intel/xess | Pinned submodule |
| NVAPI | https://github.com/NVIDIA/nvapi | Pinned submodule |
| DLSS / DLSSG / DLSSNR | https://github.com/NVIDIA/DLSS | Review binary version and license |
| Streamline | https://github.com/NVIDIA-RTX/Streamline | Review binary version and license |
| Nukem FG replacement | https://github.com/Nukem9/dlssg-to-fsr3 | Optional external binary |
| Arturs FG replacement | https://github.com/artur-graniszewski/DLSS-Enabler | Optional external binary |
| DLSSNR color composition | https://github.com/clshortfuse/renodx | Preserve `Licenses/RenoDX_ATTRIBUTION.txt` |

## Build and validation

Use Visual Studio 2022: `MSBuild.exe OptiScaler.sln /m
/p:Configuration=Release /p:Platform=x64`. This checks compilation, not game
compatibility. Test the manager with `dotnet test OptiScalerManager/OptiScalerManager.sln
-c Release`. Test experimental features only in an offline, non-anticheat game
and keep a recoverable copy of the game's original files.
