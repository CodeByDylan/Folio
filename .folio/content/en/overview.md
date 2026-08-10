# Overview

A read-only HTTP API that assembles a portfolio from two sources: what GitHub knows about a
repository, and what that repository says about itself in a `.folio` folder committed beside its
code.

TOML describes structure, markdown carries the prose, and every string a visitor reads lives under
the locale it belongs to. Adding a language adds files; it never edits existing ones.

The API states facts and leaves policy to the frontend. It reports what it found and where it came
from — including that a Dutch string fell back to English — and never decides whether that is worth
showing.
