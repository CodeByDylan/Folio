# Story

A portfolio goes stale the moment describing the work becomes a separate job from doing it. The
projects keep changing; the paragraph about them sits somewhere the change never reaches.

So the description moves into the repository it describes, in the same commit as the change it
describes. A project documents itself, next to its code, and the site reads whatever that repository
says today.

There are two copies of the format and no overlap between them. The central one describes the site:
which locales exist, which projects appear, in what order. The per-repo one describes exactly one
project. Neither overrides the other, because they never say the same thing — which is what stops a
debugging session from beginning with "which file won?".

Everything is rebuilt into one immutable snapshot on a timer, and requests read that graph. No
request reaches GitHub, so a slow upstream and a rate limit stop being the visitor's problem. A
refresh that fails leaves the previous snapshot serving.

The cost, stated plainly: a project with nothing to say shows nothing. The API will not invent a
description, and a repository that has not been given one appears with only what GitHub reports.
