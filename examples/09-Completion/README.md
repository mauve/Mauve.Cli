# 09 - Completion

Both projects demonstrate command, option and async value completion with
logical-token requests. The ordinary command only prints a search action.

```sh
dotnet run --project examples/09-Completion/Declarative -- --demo-completion
dotnet run --project examples/09-Completion/Manual -- --demo-completion
dotnet run --project examples/09-Completion/Declarative -- '[completion:bash]'
```

`[completion:bash]` prints an opt-in Bash registration script. Other supported
registration modes are `[completion:zsh]`, `[completion:fish]` and
`[completion:powershell]`. The declarative project uses generated declarations;
the manual project does not reference the generator.
