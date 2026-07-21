# PeerCast Nova Distribution Terms Guard

Whenever an FFmpeg backend is available, every PeerCast Nova release, installer, EULA, terms screen, About surface, and translated legal text must satisfy this gate.

## Required statements

- FFmpeg is separate software and is not licensed under PeerCast Nova's Apache License 2.0.
- The applicable GNU license texts are included and readily readable.
- Host-supplied runtime declarations are host attestations and are not Nova review approvals.
- FFmpeg is a trademark of Fabrice Bellard, originator of the FFmpeg project.

## Prohibited terms

No Nova-controlled term may prohibit or contractually restrict reverse engineering performed to debug modifications to the LGPL libraries. This includes broad boilerplate bans on reverse engineering, decompilation, disassembly, circumvention, or modification when they fail to preserve the LGPL debugging exception.

If a platform store, installer framework, packaging tool, or third-party EULA injects conflicting terms, the FFmpeg-enabled distribution must not be published until the conflict is removed or a legally reviewed exception preserves the required user rights.

## Release verification

The release review must search all distributable terms and translations for at least:

```text
reverse engineer
reverse engineering
decompile
disassemble
circumvent
modify
modification
```

Each match must be reviewed in context. The release record must identify the reviewed files and state that no conflicting prohibition remains.

This guard is a PeerCast Nova release-engineering requirement and not a complete legal analysis.
