import unittest
from unittest.mock import patch
import subprocess
import tempfile
from pathlib import Path

from ci_scope import PLAN, changed_paths, select_checks


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

    def plan(self, files, web=None, server=None, compose=False):
        return dict(files=files, web=web or [], server=server or [], compose=compose,
                    reason="Affected callers and relevant regressions reviewed for this change.")

    def test_ci_and_document_changes_do_not_run_application_tests(self):
        result = select_checks(["README.md", "docs/STATUS.md", ".github/workflows/ci.yml"], {})
        self.assertEqual(result, dict(web=False, server=False, compose=False))

    def test_web_server_and_deployment_are_independent(self):
        web = ["src/features/services/Services.test.tsx"]
        server = ["Server.Tests.AppointmentReminderTests"]
        for files, kwargs, expected in [
            (["src/Web/src/features/services/Services.tsx"], dict(web=web), (True, False, False)),
            (["src/Server/Features/Scheduling/AppointmentReminders.cs"], dict(server=server), (False, True, False)),
            (["deploy/compose.local.yaml"], dict(compose=True), (False, False, True)),
            (["src/Web/package-lock.json", "src/Server/Program.cs"], dict(web=web, server=server), (True, True, False)),
        ]:
            with self.subTest(files=files):
                result = select_checks(files + [PLAN], self.plan(files, **kwargs))
                self.assertEqual(tuple(result.values()), expected)

    def test_missing_stale_empty_or_unknown_plan_is_rejected(self):
        files = ["src/Server/Program.cs"]
        for paths, plan in [
            (files, self.plan(files)),
            (files + [PLAN], self.plan(["src/Server/Other.cs"])),
            (files + [PLAN], self.plan(files)),
            (files + [PLAN], self.plan(files, server=["Server.Tests.MissingTests"])),
            (["unknown.config", PLAN], self.plan(["unknown.config"])),
            (["src/Web/package.json", PLAN], self.plan(["src/Web/package.json"])),
            (["deploy/compose.local.yaml", PLAN], self.plan(["deploy/compose.local.yaml"])),
        ]:
            with self.subTest(plan=plan):
                with self.assertRaises(ValueError):
                    select_checks(paths, plan)

    def test_changed_tests_cannot_be_omitted_or_replaced_with_wildcards(self):
        for files, plan in [
            (["tests/Server.Tests/AppointmentReminderTests.cs"],
             dict(server=["Server.Tests.HealthEndpointsTests"])),
            (["src/Web/src/features/services/Services.test.tsx"],
             dict(web=["src/features/public-booking/PublicBooking.test.tsx"])),
            (["src/Server/Program.cs"], dict(server=["*"])),
            (["src/Web/src/App.tsx"], dict(web=["*"])),
        ]:
            with self.subTest(plan=plan):
                with self.assertRaises(ValueError):
                    select_checks(files + [PLAN], self.plan(files, **plan))

    def test_missing_or_unknown_range_blocks_instead_of_running_everything(self):
        for base, head, event in [("0" * 40, "b" * 40, "push"),
                                  ("", "b" * 40, "push"),
                                  ("a" * 40, "b" * 40, "other")]:
            with self.assertRaises(ValueError):
                changed_paths(base, head, event)
        with patch("ci_scope.subprocess.check_output", side_effect=subprocess.CalledProcessError(1, "git")):
            with self.assertRaises(subprocess.CalledProcessError):
                changed_paths("a" * 40, "b" * 40, "push")

    def test_event_ranges_and_rename_safety(self):
        for event, separator in [("push", ".."), ("pull_request", "...")]:
            with patch("ci_scope.subprocess.check_output", return_value=b"docs/STATUS.md\0") as git:
                self.assertEqual(changed_paths("a" * 40, "b" * 40, event), ["docs/STATUS.md"])
                self.assertIn("--no-renames", git.call_args.args[0])
                self.assertIn("a" * 40 + separator + "b" * 40, git.call_args.args[0])
        with patch("ci_scope.subprocess.check_output", return_value=b"README.md\0src/tool.py\0"):
            with self.assertRaises(ValueError):
                select_checks(changed_paths("a" * 40, "b" * 40, "push"), {})


if __name__ == "__main__":
    unittest.main()
