import hashlib
import os
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import activate_release as release


def write(root, name, content):
    path = root / name
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content)
    return path


@unittest.skipUnless(os.name == "posix", "The production host uses Linux and rsync.")
class ReleaseTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.live, self.published = self.root / "live", self.root / "published"
        for directory in release.OWNED_WEB_DIRS:
            write(self.live, f"wwwroot/{directory}/retired", "retired")
        self.upload = write(self.live, "wwwroot/generated/photo", "upload").stat()
        write(self.live, "wwwroot/assets/content-images/legacy", "host artwork")
        write(self.live, "AppData/subscriptions.txt", "original")
        write(self.live, "config.json", "live configuration").chmod(0o640)
        write(self.live, "config.json.bak-1", "backup")
        write(self.live, "app.dll", "old")
        write(self.published, "app.dll", "new")
        write(self.published, "wwwroot/js/check-in.js", "new script")
        write(self.published, "wwwroot/css/main.css", "new style")
        write(self.published, "AppData/subscriptions.txt", "packaged default")
        write(self.published, "wwwroot/generated/example", "packaged image")
        write(self.published, "config.json", "packaged default")
        digest = hashlib.sha256(b"new script").hexdigest()
        self.manifest = write(self.root, "manifest", f"{digest}  ./check-in.js\n")

    def test_preparation_and_switch_keep_late_writes_and_prune_only_owned_assets(self):
        candidate = self.root / "candidate"
        release.stage_release(self.live, self.published, candidate, self.manifest)
        self.assertEqual((self.live / "app.dll").read_text(), "old")
        self.assertFalse((candidate / "config.json").exists())
        self.assertFalse((candidate / "wwwroot/generated").exists())
        write(self.live, "wwwroot/generated/just-uploaded", "late")
        release.switch_release(self.live, candidate, self.root / "previous")

        self.assertEqual((self.live / "app.dll").read_text(), "new")
        self.assertEqual((self.live / "wwwroot/css/main.css").read_text(), "new style")
        self.assertEqual((self.live / "wwwroot/assets/content-images/legacy").read_text(), "host artwork")
        self.assertEqual((self.live / "config.json").read_text(), "live configuration")
        self.assertEqual((self.live / "config.json").stat().st_mode & 0o777, 0o640)
        self.assertEqual((self.live / "config.json.bak-1").read_text(), "backup")
        self.assertEqual((self.live / "AppData/subscriptions.txt").read_text(), "original")
        self.assertEqual((self.live / "wwwroot/generated/just-uploaded").read_text(), "late")
        self.assertEqual((self.live / "wwwroot/generated/photo").stat().st_ino, self.upload.st_ino)
        self.assertFalse((self.live / "wwwroot/generated/example").exists())
        for directory in release.OWNED_WEB_DIRS:
            self.assertFalse((self.live / "wwwroot" / directory / "retired").exists())

    def test_partial_switch_restores_all_runtime_paths(self):
        candidate = self.root / "candidate"
        release.stage_release(self.live, self.published, candidate, self.manifest)
        (candidate / "wwwroot/generated").mkdir()  # The last protected path collides.
        with self.assertRaises(FileExistsError):
            release.switch_release(self.live, candidate, self.root / "previous")
        self.assertEqual((self.live / "app.dll").read_text(), "old")
        self.assertEqual((self.live / "AppData/subscriptions.txt").read_text(), "original")
        self.assertEqual((self.live / "config.json").read_text(), "live configuration")
        self.assertEqual((self.live / "wwwroot/generated/photo").stat().st_ino, self.upload.st_ino)
        self.assertFalse((self.root / "previous").exists())

    def test_invalid_artifact_never_stops_the_site(self):
        write(self.published, "wwwroot/js/check-in.js", "corrupt")
        with patch.object(release, "service") as service, self.assertRaises(subprocess.CalledProcessError):
            release.deploy(self.live, self.published, self.manifest, ["true"])
        service.assert_not_called()
        self.assertEqual((self.live / "app.dll").read_text(), "old")
        self.assertEqual(list(self.root.glob(".release-*")), [])

    def test_failed_filesystem_recovery_keeps_both_trees_for_repair(self):
        rename = Path.rename

        def fail_handoff_and_restore(source, destination):
            if source == self.live / "wwwroot/generated" or (destination == self.live / "AppData"):
                raise OSError("unavailable filesystem")
            return rename(source, destination)

        with patch.object(Path, "rename", fail_handoff_and_restore), patch.object(release, "service") as service:
            with self.assertRaises(release.ReleaseRecoveryError):
                release.deploy(self.live, self.published, self.manifest, ["true"])
        service.assert_called_once_with("stop")
        work, = self.root.glob(".release-*")
        self.assertEqual((work / "next/AppData/subscriptions.txt").read_text(), "original")
        self.assertEqual((self.live / "app.dll").read_text(), "old")
        self.assertEqual((self.live / "wwwroot/generated/photo").stat().st_ino, self.upload.st_ino)

    def test_failed_live_probe_rolls_back_code_and_keeps_writes_from_new_release(self):
        transitions = []

        def record_service(action):
            transitions.append((action, (self.live / "app.dll").read_text()))

        verify = ["sh", "-ec", 'printf new > "$1/AppData/subscriptions.txt"; '
                  'printf upload > "$1/wwwroot/generated/new"; exit 23', "verify", str(self.live)]
        with patch.object(release, "service", side_effect=record_service), self.assertRaises(subprocess.CalledProcessError) as error:
            release.deploy(self.live, self.published, self.manifest, verify)
        self.assertEqual(error.exception.returncode, 23)
        self.assertEqual(transitions, [("stop", "old"), ("start", "new"), ("stop", "new"), ("start", "old")])
        self.assertEqual((self.live / "AppData/subscriptions.txt").read_text(), "new")
        self.assertEqual((self.live / "wwwroot/generated/new").read_text(), "upload")
        self.assertEqual(list(self.root.glob(".release-*")), [])

    def test_failed_start_restores_previous_release_before_restarting(self):
        def start_only_old_release(action):
            if action == "start" and (self.live / "app.dll").read_text() == "new":
                raise RuntimeError("candidate failed to start")

        with patch.object(release, "service", side_effect=start_only_old_release) as service:
            with self.assertRaisesRegex(RuntimeError, "candidate failed to start"):
                release.deploy(self.live, self.published, self.manifest, ["true"])
        self.assertEqual([call.args[0] for call in service.call_args_list], ["stop", "start", "stop", "start"])
        self.assertEqual((self.live / "app.dll").read_text(), "old")
        self.assertEqual((self.live / "wwwroot/generated/photo").stat().st_ino, self.upload.st_ino)
        self.assertEqual(list(self.root.glob(".release-*")), [])


if __name__ == "__main__":
    unittest.main()
