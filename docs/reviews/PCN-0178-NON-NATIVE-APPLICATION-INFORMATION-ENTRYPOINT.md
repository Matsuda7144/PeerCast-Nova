# PCN-0178: Non-native Application Information Entrypoint

## Status

Implemented and verified as a non-physical `Nova.App` feature.

## Implemented behavior

- no arguments preserve the existing shared Windows startup composition;
- `--help` and `-h` print finite usage text and return success;
- `--about` prints the exact retained legal/About model and returns success;
- unknown or multiple arguments return usage error `2`;
- every information or rejection path returns before application-host startup.

## Verification

Managed tests prove command output, exit-code mapping, exact About material
projection, and zero startup invocations for help, About, and invalid
arguments. Solution build, tests, and format verification remain the routine
integration gates.

No readiness or physical-run authorization is required or created.

## Remaining work

The application still has no general playback GUI, channel selection, settings,
or complete user workflow. This slice must not be reported as completion of all
user-facing application features.
