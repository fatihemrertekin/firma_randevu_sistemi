import unittest
from unittest.mock import patch
import subprocess
import tempfile
from pathlib import Path

from ci_scope import documentation_only, full_checks


class ScopeTests(unittest.TestCase):
    def test_document_validator_rejects_broken_local_link(self):
        script = Path(__file__).with_name("Validate-Documentation.ps1").resolve()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            subprocess.run(["git", "init", "-q", directory], check=True)
            document = root / "README.md"
            document.write_text("# Example\n\n[Working](README.md#example)\n", encoding="utf-8")
            subprocess.run(["git", "-C", directory, "add", "README.md"], check=True)
            command = ["pwsh", "-NoProfile", "-File", str(script), "-RepositoryRoot", directory]
            valid = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(valid.returncode, 0, valid.stderr)
            document.write_text("[Broken](missing.md)\n", encoding="utf-8")
            invalid = subprocess.run(command, capture_output=True, text=True)
            self.assertNotEqual(invalid.returncode, 0)

    def test_only_explicit_documentation_is_lightweight(self):
        self.assertTrue(documentation_only(["README.md", "DESIGN.md", "docs/plans/P05.md"]))
        for path in ["src/Web/src/App.tsx", "tests/Server.Tests/Test.cs", "deploy/Dockerfile",
                     ".github/workflows/ci.yml", "src/Web/package-lock.json", "AGENTS.md",
                     "docs/tool.ps1", "docs/image.svg", "unknown.md"]:
            with self.subTest(path=path):
                self.assertFalse(documentation_only(["docs/STATUS.md", path]))
        self.assertFalse(documentation_only([]))

    def test_missing_or_unknown_range_runs_full(self):
        for base, head, event in [("0" * 40, "b" * 40, "push"),
                                  ("", "b" * 40, "push"),
                                  ("a" * 40, "b" * 40, "other")]:
            self.assertTrue(full_checks(base, head, event))
        with patch("ci_scope.subprocess.check_output", side_effect=subprocess.CalledProcessError(1, "git")):
            self.assertTrue(full_checks("a" * 40, "b" * 40, "push"))

    def test_event_ranges_and_rename_safety(self):
        for event, separator in [("push", ".."), ("pull_request", "...")]:
            with patch("ci_scope.subprocess.check_output", return_value=b"docs/STATUS.md\0") as git:
                self.assertFalse(full_checks("a" * 40, "b" * 40, event))
                self.assertIn("--no-renames", git.call_args.args[0])
                self.assertIn("a" * 40 + separator + "b" * 40, git.call_args.args[0])
        with patch("ci_scope.subprocess.check_output", return_value=b"README.md\0src/tool.py\0"):
            self.assertTrue(full_checks("a" * 40, "b" * 40, "push"))


if __name__ == "__main__":
    unittest.main()
