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
