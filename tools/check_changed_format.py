"""Check changed C++ lines without reformatting pre-existing Aurora code.

The original whole-tree action reports dozens of files that were already
unformatted before this branch. New or edited lines are still checked.
"""

import os
import re
import subprocess
import sys
from pathlib import Path


def git(*args: str) -> str:
    return subprocess.check_output(["git", *args], text=True, encoding="utf-8")


def main() -> int:
    base = os.environ.get("BASE_SHA", "")
    head = os.environ.get("HEAD_SHA", "HEAD")
    if not base or set(base) == {"0"}:
        base = git("rev-parse", f"{head}^").strip()

    diff = git("-c", "core.quotePath=false", "diff", "--unified=0", "--no-ext-diff", "--no-renames", base, head, "--", "OptiScaler")
    changed: dict[str, list[tuple[int, int]]] = {}
    current: str | None = None
    for line in diff.splitlines():
        if line.startswith("+++ "):
            match = re.fullmatch(r"\+\+\+ b/(OptiScaler/.+\.(?:c|cc|cpp|h|hpp))", line)
            current = match.group(1) if match else None
        elif current and line.startswith("@@ "):
            match = re.match(r"@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@", line)
            if match:
                start = int(match.group(1))
                count = int(match.group(2) or "1")
                if count:
                    changed.setdefault(current, []).append((start, start + count - 1))

    failed = False
    for name, ranges in sorted(changed.items()):
        if not Path(name).is_file():
            continue
        command = ["clang-format", "--dry-run", "--Werror", "--style=file"]
        command += [f"--lines={start}:{end}" for start, end in ranges]
        command.append(name)
        result = subprocess.run(command, check=False)
        if result.returncode:
            print(f"Formatting check failed on changed lines in {name}", file=sys.stderr)
            failed = True
    print(f"Checked changed lines in {len(changed)} C/C++ files.")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
