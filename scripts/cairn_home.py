#!/usr/bin/env python3
"""Cairn.Core.CairnHome's root precedence, shared by launch, login and diagnostics."""
import os
import sys


def cairn_home():
    override = os.environ.get("CAIRN_HOME", "").strip()
    if override:
        return override
    default = (os.environ.get("CAIRN_DEFAULT_HOME", "").strip()
               or os.path.join(os.path.expanduser("~"), ".cairn"))
    pointer = os.path.join(default, "home")
    try:
        with open(pointer) as source:
            root = source.read().strip()
    except FileNotFoundError:
        return default
    except OSError as error:
        print(f"{pointer}: {error}; using {default}", file=sys.stderr)
        return default
    if not root or not os.path.isabs(root):
        print(f"{pointer}: expected an absolute path; using {default}", file=sys.stderr)
        return default
    return root


if __name__ == "__main__":
    print(cairn_home())
