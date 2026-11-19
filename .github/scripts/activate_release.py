"""Install a staged production release, keeping live application data through rollback."""

import os
import shutil
import signal
import subprocess
import sys
import tempfile
from contextlib import ExitStack
from pathlib import Path


RUNTIME_PATHS = ("config.json", "config.json.bak*", "AppData", "wwwroot/generated")
OWNED_WEB_DIRS = ("athletes", "js", "css", "onboarding", "research", "helstab-kihivas")
SERVICE = "longevityworldcup.service"


def stage_release(live, published, candidate, manifest):
    candidate.mkdir()
    runtime_excludes = ["--exclude=/" + pattern for pattern in RUNTIME_PATHS]
    owned_excludes = ["--exclude=/wwwroot/" + name for name in OWNED_WEB_DIRS]

    # Build from published bytes first. Only host files outside the pruned directories
    # carry forward. Neither pass needs to open a live upload or a production secret.
    subprocess.run(["rsync", "-a", "--no-owner", "--no-group", *runtime_excludes,
                    f"{published}/", f"{candidate}/"], check=True)
    subprocess.run(["rsync", "-a", "--ignore-existing", *runtime_excludes, *owned_excludes,
                    f"{live}/", f"{candidate}/"], check=True)
    subprocess.run(["sha256sum", "--check", "--quiet", manifest],
                   cwd=candidate / "wwwroot/js", check=True)


def switch_release(live, incoming, outgoing):
    # All paths share a filesystem. At shutdown, even a large upload directory moves
    # in one rename. Reverse each completed rename if a later one fails.
    mask = signal.pthread_sigmask(signal.SIG_BLOCK, {signal.SIGINT, signal.SIGHUP, signal.SIGTERM})
    try:
        with ExitStack() as undo:
            def move(source, destination):
                if os.path.lexists(destination):
                    raise FileExistsError(destination)
                source.rename(destination)
                undo.callback(destination.rename, source)

            move(live, outgoing)
            for pattern in RUNTIME_PATHS:
                for path in outgoing.glob(pattern):
                    move(path, incoming / path.relative_to(outgoing))
            move(incoming, live)
            undo.pop_all()
    finally:
        signal.pthread_sigmask(signal.SIG_SETMASK, mask)


def service(action):
    subprocess.run(["systemctl", action, SERVICE], check=True)


def deploy(live, published, manifest, verify_command):
    work = Path(tempfile.mkdtemp(prefix=".release-", dir=live.parent))
    candidate, previous = work / "next", work / "previous"
    keep_for_recovery = False
    try:
        stage_release(live, published, candidate, manifest)
        try:
            service("stop")
            switch_release(live, candidate, previous)
            service("start")
            subprocess.run(verify_command, check=True)
        except BaseException:
            try:
                if previous.exists():
                    print("Restoring the previous release.", flush=True)
                    service("stop")
                    switch_release(live, previous, candidate)
                service("start")
            except BaseException:
                keep_for_recovery = True
                raise
            raise
    finally:
        if keep_for_recovery:
            print(f"Release files retained for recovery at {work}", file=sys.stderr)
        else:
            shutil.rmtree(work)


if __name__ == "__main__":
    def interrupted(signum, _frame):
        sys.exit(128 + signum)

    for sig in (signal.SIGHUP, signal.SIGTERM):
        signal.signal(sig, interrupted)
    root, build, checksums, *verify = sys.argv[1:]
    if not verify:
        sys.exit("Expected the command that verifies the running release.")
    deploy(Path(root), Path(build), Path(checksums), verify)
