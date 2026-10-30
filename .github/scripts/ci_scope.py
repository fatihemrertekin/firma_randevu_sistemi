"""Require a reviewed, current test plan for product changes; never guess coverage."""

import json
import os
from pathlib import Path
import re
import subprocess

PLAN = ".github/test-plan.json"
CI_FILES = {PLAN, ".github/workflows/ci.yml", ".github/scripts/ci_scope.py",
            ".github/scripts/test_ci_scope.py", ".github/scripts/Validate-Documentation.ps1",
            ".gitignore", "AGENTS.md", "README.md", "DESIGN.md"}


def product_paths(paths):
    return {p for p in paths if p not in CI_FILES
            and not (p.startswith("docs/") and p.endswith(".md"))}


def select_checks(paths, plan, root=Path(".")):
    product = product_paths(paths)
    if not product:
        return dict(web=False, server=False, compose=False)
    if PLAN not in paths or set(plan.get("files", [])) != product:
        raise ValueError("Update .github/test-plan.json: files must match all changed product paths.")
    if not isinstance(plan.get("reason"), str) or len(plan["reason"].strip()) < 20:
        raise ValueError("Explain affected callers and why these tests cover the change.")
    web, server, compose = plan.get("web"), plan.get("server"), plan.get("compose")
    if not isinstance(web, list) or not isinstance(server, list) or type(compose) is not bool:
        raise ValueError("web/server must be lists; compose must be boolean.")
    for name in web:
        if not isinstance(name, str) or not re.fullmatch(r"src/[\w/-]+\.test\.tsx", name) \
                or not (root / "src/Web" / name).is_file():
            raise ValueError(f"Unknown web test: {name}")
    for name in server:
        if not isinstance(name, str) or not re.fullmatch(r"Server\.Tests\.[A-Za-z][A-Za-z0-9]*Tests", name) \
                or not (root / "tests/Server.Tests" / f"{name.split('.')[-1]}.cs").is_file():
            raise ValueError(f"Unknown server test class: {name}")
    if any(p.startswith("src/Web/") for p in product) and not web:
        raise ValueError("Web changes require related web tests.")
    if any(p.startswith(("src/Server/", "src/Operator/", "tests/Server.Tests/")) for p in product) and not server:
        raise ValueError("Server changes require related server tests.")
    if any(p.startswith("deploy/") for p in product) and not compose:
        raise ValueError("Deployment changes require Compose acceptance.")
    for path in product:
        if (root / path).is_file() and path.endswith(".test.tsx") and path.startswith("src/Web/"):
            if path.removeprefix("src/Web/") not in web:
                raise ValueError("Include changed web tests in the plan.")
        if (root / path).is_file() and re.fullmatch(r"tests/Server.Tests/[A-Za-z0-9]+Tests.cs", path):
            if f"Server.Tests.{Path(path).stem}" not in server:
                raise ValueError("Include changed server test classes in the plan.")
    if not (web or server or compose):
        raise ValueError("Product changes require at least one relevant acceptance check.")
    return dict(web=bool(web), server=bool(server), compose=compose)


def changed_paths(base, head, event):
    if event not in {"push", "pull_request"} or not all(
        re.fullmatch(r"[0-9a-f]{40}", sha) and sha != "0" * 40 for sha in (base, head)
    ):
        raise ValueError("Cannot determine change range; select tests before continuing.")
    comparison = f"{base}...{head}" if event == "pull_request" else f"{base}..{head}"
    return [p for p in subprocess.check_output(
        ["git", "diff", "--no-renames", "--name-only", "-z", comparison, "--"]
    ).decode("utf-8").split("\0") if p]


if __name__ == "__main__":
    paths = changed_paths(os.getenv("BASE_SHA", ""), os.getenv("HEAD_SHA", ""), os.getenv("CI_EVENT", ""))
    checks = select_checks(paths, json.loads(Path(PLAN).read_text(encoding="utf-8")))
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        for name, enabled in checks.items():
            output.write(f"{name}={str(enabled).lower()}\n")
    print(json.dumps({"checks": checks, "plan": PLAN, "changed_files": len(paths)}))
