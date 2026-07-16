# PCN-0050 Decoder Backend Boundary

The backend boundary sits behind the existing `IPlaybackDecoderFactory` used by
`PlaybackDecoderRunner`:

```text
classified container
  -> PlaybackDecoderBackendFactory
  -> immutable capability descriptors
  -> one selected IPlaybackDecoderBackend
  -> existing IPlaybackDecoder lifecycle
  -> existing IPlaybackDecodedFrameSink
```

Enumeration is side-effect free. Only the selected backend is initialized.
Priority is bounded and duplicate IDs are rejected. IDs are restricted to
ASCII letters, digits, dots, and hyphens so they are safe fixed diagnostic
labels rather than paths or user-controlled media metadata.

The core deliberately provides no plugin search path, dynamic assembly loader,
native handle, package resolver, or backend-specific options. Those belong to a
future host integration with its own provenance review. 0.0.5f adds no codec
package and bundles no native binary.

Initialization failure is terminal for the selected backend. Automatic fallback
could hide a broken or mislicensed installation and make behavior dependent on
machine state. Unsupported capability is the only condition that continues
selection; if no backend supports the container, the existing
`DecoderUnsupported` result is used.
