# 08 - Option packs

Both projects compose a reusable network pack with a nested retry pack. The
`fetch` and `inspect` commands share that pack, read invocation-local values,
and only print the requested operation.

```sh
dotnet run --project examples/08-OptionPacks/Declarative -- fetch --endpoint https://example.test --retries 2
dotnet run --project examples/08-OptionPacks/Manual -- inspect --endpoint https://example.test --retries 2
```

`Declarative` uses generated declarations; `Manual` builds the equivalent
model directly and has no generator reference.

Declarative packs define options as `[CliOption]` get/init properties. A pack
constructor parameter includes and injects another pack; command constructors
include and receive packs in the same way. Optional help-group title and
description belong to `[CliOptionPack]`. Each owner can include a pack type
only once directly.

This replaces the earlier declarative `CliPack<TPack>` and
`CliPackOption<TValue>` attributes and generated `OptionN` handles. Migrate
option attributes to properties and inclusions to constructor parameters;
read values from the injected pack rather than looking them up by key.
Explicit builder and `OptionPackContext` users continue to use inclusion keys.
