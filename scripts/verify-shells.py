#!/usr/bin/env python3
"""Exercise generated shell adapters against the real FileTool example."""

import argparse
import os
import pathlib
import pty
import select
import shlex
import shutil
import signal
import subprocess
import tempfile
import time


ROOT = pathlib.Path(__file__).resolve().parent.parent
APP = ROOT / "examples/FileTool.Cli/bin/Release/net11.0/linux-x64/FileTool.Cli.dll"


def close_child(pid, descriptor):
    os.close(descriptor)
    try:
        finished, _ = os.waitpid(pid, os.WNOHANG)
        if not finished:
            os.kill(pid, signal.SIGTERM)
            os.waitpid(pid, 0)
    except ChildProcessError:
        pass


def interactive(shell, setup, environment, startup=0.5):
    pid, descriptor = pty.fork()
    if pid == 0:
        os.execvpe(shell[0], shell, environment)

    def exchange(text, delay=0.6):
        if text:
            os.write(descriptor, text.encode())
        time.sleep(delay)
        output = bytearray()
        while select.select([descriptor], [], [], 0.1)[0]:
            try:
                output.extend(os.read(descriptor, 65536))
            except OSError:
                break
        return output.decode(errors="replace")

    try:
        exchange("", startup)
        initial = exchange(setup + "\n", 1.0)
        if "command not found" in initial or "failed to load module" in initial:
            raise RuntimeError(f"Shell initialization failed: {initial}")
        insertion = exchange("file-tool c\t", 1.0)
        execution = exchange("source dest\n", 1.0)
        if "Copy source to dest; overwrite=False" not in execution:
            raise RuntimeError(f"Command completion failed: {insertion}\n{execution}")

        os.write(descriptor, b"exit\n")
        os.waitpid(pid, 0)
    finally:
        close_child(pid, descriptor)


def candidate_interactive(shell, setup, environment, startup=0.5, quote="", prefix=""):
    pathlib.Path(environment["MAUVE_SHELL_TEST_CAPTURE"]).unlink(missing_ok=True)
    pid, descriptor = pty.fork()
    if pid == 0:
        os.execvpe(shell[0], shell, environment)

    def exchange(text, delay=0.8):
        if text:
            os.write(descriptor, text.encode())
        time.sleep(delay)
        output = bytearray()
        while select.select([descriptor], [], [], 0.1)[0]:
            try:
                output.extend(os.read(descriptor, 65536))
            except OSError:
                break
        return output.decode(errors="replace")

    try:
        exchange("", startup)
        exchange(setup + "\n", 1.0)
        insertion = exchange(f"file-tool copy --overwrite={quote}{prefix}\t", 1.5)
        execution = exchange("source dest\n", 1.0)
        marker = pathlib.Path(environment["MAUVE_SHELL_TEST_MARKER"])
        capture = pathlib.Path(environment["MAUVE_SHELL_TEST_CAPTURE"])
        if marker.exists():
            raise RuntimeError("The shell executed candidate text.")
        expected = "--overwrite=" + environment["MAUVE_SHELL_TEST_VALUE"]
        if not capture.exists() or capture.read_text(encoding="utf-8") != expected:
            raise RuntimeError(f"Candidate was not preserved: {insertion}\n{execution}")
        os.write(descriptor, b"exit\n")
        os.waitpid(pid, 0)
    finally:
        close_child(pid, descriptor)


def powershell(executable, environment):
    script = r"""
    (& file-tool '[completion:powershell]' | Out-String) | Invoke-Expression
    $line = 'file-tool c'
    $result = TabExpansion2 -inputScript $line -cursorColumn $line.Length
    if ($result.CompletionMatches.Count -ne 1 -or $result.CompletionMatches[0].CompletionText -ne "'copy'") { exit 1 }
    $completed = $line.Substring(0, $result.ReplacementIndex) + $result.CompletionMatches[0].CompletionText + ' source dest'
    $output = & ([scriptblock]::Create($completed))
    if ($output -ne 'Copy source to dest; overwrite=False') { exit 2 }
    $line = 'file-tool cXYZ'
    $result = TabExpansion2 -inputScript $line -cursorColumn 11
    if ($result.CompletionMatches.Count -ne 1 -or $result.ReplacementLength -ne 4) { exit 4 }
    $completed = $line.Substring(0, $result.ReplacementIndex) + $result.CompletionMatches[0].CompletionText +
        $line.Substring($result.ReplacementIndex + $result.ReplacementLength) + ' source dest'
    $output = & ([scriptblock]::Create($completed))
    if ($output -ne 'Copy source to dest; overwrite=False') { exit 5 }
    """
    subprocess.run([executable, "-NoProfile", "-NonInteractive", "-Command", script], env=environment, check=True)

    script = r"""
    (& file-tool '[completion:powershell]' | Out-String) | Invoke-Expression
    $line = 'file-tool copy --overwrite='
    $result = TabExpansion2 -inputScript $line -cursorColumn $line.Length
    if ($result.CompletionMatches.Count -ne 1) { exit 3 }
    $completed = $line.Substring(0, $result.ReplacementIndex) + $result.CompletionMatches[0].CompletionText + ' source dest'
    & ([scriptblock]::Create($completed)) 2>$null
    """
    for candidate in candidates(environment):
        test_environment = candidate_environment(environment, "powershell", candidate)
        subprocess.run([executable, "-NoProfile", "-NonInteractive", "-Command", script], env=test_environment, check=True)
        assert_candidate(test_environment)


def candidates(environment):
    marker = environment["MAUVE_SHELL_TEST_MARKER"]
    return ["hello world", "a'b", "😀star", f"$(touch {marker})"]


def candidate_environment(environment, shell, candidate):
    test_environment = environment.copy()
    test_environment["MAUVE_SHELL_TEST_VALUE"] = candidate
    test_environment["MAUVE_SHELL_TEST_CAPTURE"] = str(
        pathlib.Path(environment["MAUVE_SHELL_TEST_MARKER"]).parent
        / f"capture-{shell}-{len(candidate)}"
    )
    return test_environment


def assert_candidate(environment):
    marker = pathlib.Path(environment["MAUVE_SHELL_TEST_MARKER"])
    capture = pathlib.Path(environment["MAUVE_SHELL_TEST_CAPTURE"])
    if marker.exists() or not capture.exists():
        raise RuntimeError("Candidate execution was unsafe or did not run.")
    if capture.read_text(encoding="utf-8") != "--overwrite=" + environment["MAUVE_SHELL_TEST_VALUE"]:
        raise RuntimeError("The shell changed the candidate value.")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--require-all", action="store_true")
    args = parser.parse_args()
    if not APP.exists():
        raise SystemExit(f"Build FileTool.Cli first: {APP}")

    shells = {
        "bash": os.environ.get("MAUVE_BASH") or shutil.which("bash"),
        "zsh": os.environ.get("MAUVE_ZSH") or shutil.which("zsh"),
        "fish": os.environ.get("MAUVE_FISH") or shutil.which("fish"),
        "powershell": os.environ.get("MAUVE_PWSH") or shutil.which("pwsh"),
    }
    missing = [name for name, executable in shells.items() if not executable]
    if missing:
        print("Unavailable shells: " + ", ".join(missing), flush=True)
        if args.require_all:
            raise SystemExit(1)

    with tempfile.TemporaryDirectory(prefix="mauve-shell-check-") as directory:
        directory = pathlib.Path(directory)
        wrapper = directory / "file-tool"
        wrapper.write_text(
            "#!/bin/sh\n"
            "if [ \"$1\" = '[completion:query-v1]' ] && [ \"${MAUVE_SHELL_TEST_VALUE+x}\" = x ]; then\n"
            "  value=$MAUVE_SHELL_TEST_VALUE\n"
            "  if [ \"$3\" != bash ]; then value=\"--overwrite=$value\"; fi\n"
            "  printf x; printf '%s' \"$value\" | base64 | tr -d '\\n'; printf '\\n'; exit 0\n"
            "fi\n"
            "if [ \"$1\" = copy ] && [ \"${MAUVE_SHELL_TEST_CAPTURE+x}\" = x ]; then\n"
            "  printf '%s' \"$2\" > \"$MAUVE_SHELL_TEST_CAPTURE\"; exit 0\n"
            "fi\n"
            f"exec {shlex.quote(shutil.which('dotnet'))} {shlex.quote(str(APP))} \"$@\"\n",
            encoding="utf-8",
        )
        wrapper.chmod(0o755)
        environment = os.environ.copy()
        environment["PATH"] = str(directory) + os.pathsep + environment["PATH"]
        environment["MAUVE_SHELL_TEST_MARKER"] = str(directory / "injection-marker")
        setup = {
            "bash": "eval \"$(file-tool '[completion:bash]')\"",
            "zsh": "eval \"$(file-tool '[completion:zsh]')\"",
            "fish": "file-tool '[completion:fish]' | source",
        }
        if environment.get("MAUVE_ZSH_FPATH") and environment.get("MAUVE_ZSH_MODULE_PATH"):
            setup["zsh"] = (
                "module_path=($MAUVE_ZSH_MODULE_PATH); "
                "fpath=($MAUVE_ZSH_FPATH/Completion $MAUVE_ZSH_FPATH/Completion/{Base,Zsh,Unix} $MAUVE_ZSH_FPATH/Misc); "
                + setup["zsh"]
            )
        for name in ("bash", "zsh", "fish"):
            executable = shells[name]
            if not executable:
                continue
            switches = {"bash": ["--noprofile", "--norc", "-i"], "zsh": ["-f", "-i"], "fish": ["--no-config", "-i"]}[name]
            command = [executable, *switches]
            startup = 3.0 if name == "fish" else 0.5
            interactive(command, setup[name], environment, startup)
            for candidate in candidates(environment):
                test_environment = candidate_environment(environment, name, candidate)
                candidate_interactive(command, setup[name], test_environment, startup)
                if name == "bash":
                    candidate_interactive(command, setup[name], test_environment, startup, quote="'")
                    candidate_interactive(command, setup[name], test_environment, startup, quote='"')
                    if candidate == "hello world":
                        candidate_interactive(command, setup[name], test_environment, startup, prefix="h")
                        candidate_interactive(command, setup[name], test_environment, startup, prefix="hello\\ w")
                        candidate_interactive(command, setup[name], test_environment, startup, quote="'", prefix="h")
                        candidate_interactive(command, setup[name], test_environment, startup, quote='"', prefix="h")
                        candidate_interactive(
                            command, setup[name] + "; COMP_WORDBREAKS=${COMP_WORDBREAKS//=/}",
                            test_environment, startup, prefix="h"
                        )
            print(f"{name}: command and candidate completion verified", flush=True)
        if shells["powershell"]:
            powershell(shells["powershell"], environment)
            print("powershell: command completion and inert candidate verified", flush=True)


if __name__ == "__main__":
    main()
