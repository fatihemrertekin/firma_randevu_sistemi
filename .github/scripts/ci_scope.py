"""Only known documentation paths may bypass application checks."""

import os
import re
import subprocess
import sys


def documentation_only(paths):
    return bool(paths) and all(
        path in {"README.md", "DESIGN.md"}
        or (path.startswith("docs/") and path.endswith(".md"))
        for path in paths
    )


def full_checks(base, head, event):
    if event not in {"push", "pull_request"} or not all(
        re.fullmatch(r"[0-9a-f]{40}", sha) and sha != "0" * 40
        for sha in (base, head)
    ):
        return True
    comparison = f"{base}...{head}" if event == "pull_request" else f"{base}..{head}"
    try:
        changed = subprocess.check_output(
            ["git", "diff", "--no-renames", "--name-only", "-z", comparison, "--"]
        ).decode("utf-8").split("\0")
    except (subprocess.CalledProcessError, UnicodeDecodeError):
        print("Change range unavailable; running full checks.", file=sys.stderr)
        return True
    return not documentation_only([path for path in changed if path])


if __name__ == "__main__":
    full = full_checks(os.getenv("BASE_SHA", ""), os.getenv("HEAD_SHA", ""),
                       os.getenv("CI_EVENT", ""))
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"full={str(full).lower()}\n")
    print("Full application checks" if full else "Documentation checks only")
