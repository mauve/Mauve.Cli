# 07 - Option Scope

Both command trees expose `--environment` only because it is explicitly
inherited from the root. The root-local `--root-only` option is not visible
after selecting `show`; `--brief` belongs only to that child:

```sh
dotnet run --project examples/07-OptionScope/Declarative -- --environment=dev show --brief
dotnet run --project examples/07-OptionScope/Manual -- show --environment=dev
dotnet run --project examples/07-OptionScope/Declarative -- --environment=dev --root-only show
dotnet run --project examples/07-OptionScope/Manual -- --root-only show --environment=dev
```

The inherited value is accepted before or after the child command and bound
once in the selected command. A root-local option supplied before descent is
reported as out of scope instead of being rebound on the child.
