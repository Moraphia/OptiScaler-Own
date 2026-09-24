# NVIDIA runtime candidate (not a stable release)

This is a reproducible **local test candidate**, not an update to `v0.2.2`.

| Input | Source | Archive SHA-256 |
| --- | --- | --- |
| Base game package | [OptiScaler Own v0.2.2](https://github.com/Moraphia/OptiScaler-Own/releases/tag/v0.2.2) | `90D2D89D7A68742CC2096EBE6F1929B6E9113E1F6EBD2352E2E5285407F09445` |
| DLSS SDK v310.9.1 | [NVIDIA/DLSS](https://github.com/NVIDIA/DLSS/releases/tag/v310.9.1), `ngx_dlss_demo_windows.zip` | `21C0511C6B45E80C9D0338F504F4F986D261D52050F7FE2AF480E3C42581FC35` |
| Streamline SDK v2.14.1 | [NVIDIA-RTX/Streamline](https://github.com/NVIDIA-RTX/Streamline/releases/tag/v2.14.1), Windows x64 ZIP | `92C4D954631A1710DA86CA3FA8D5034F2B9503838C95FC4AE977AE149319781B` |

Run `Stage-Nvidia-Runtime-Candidate.ps1` with paths to the extracted official SDKs and base ZIP. The script validates the base ZIP hash, verifies that the two SDKs contain the same `nvngx_dlss.dll`, and requires valid NVIDIA signatures and expected file versions before staging. It replaces `nvngx_dlss.dll`, `nvngx_dlssd.dll`, `nvngx_dlssg.dll` with 310.9.1.0 and eleven `sl.*.dll` files with 2.14.1.0. It leaves `nvngx_dlssnr.dll` 310.8 and `sl.dlss_nr.dll` 2.13 untouched: the public 2.14.1 SDK does not contain `sl.dlss_nr.dll`, so this candidate is **not a uniform Streamline runtime set**. The candidate-specific JSON report lists each replacement and hash.

Static checks are not enough to publish. Before promotion, test at least one known-working D3D12 title for SR, FG 2×, MFG, NR on/off, restart persistence, and rollback. Compare logs to the v0.2.2 baseline to confirm which `sl.interposer.dll` and snippets were actually loaded. Do not copy over anti-cheat-protected games. If any NR or FG Output regression occurs, retain the v0.2.2 stable package and investigate the mixed plugin versions.

The experimental community NR model was **not** changed. A separate 310.8.SF-v2 community build exists, but it is neither an NVIDIA public SDK DLL nor a verified drop-in replacement for this branch.
