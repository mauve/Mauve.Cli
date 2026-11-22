# 04 - Value shapes

Both projects show a boolean flag, an optional nullable scalar, a collection
built from repeated `--tag` occurrences, and a final rest positional. Bare
`--filter` selects its declared null value without consuming the next token;
`--filter=recent` supplies an explicit value.

```sh
dotnet run --project examples/04-ValueShapes/Declarative -- inspect --tag alpha --filter source.txt
dotnet run --project examples/04-ValueShapes/Declarative -- inspect --tag alpha --filter=recent source.txt
dotnet run --project examples/04-ValueShapes/Manual -- inspect --tag alpha --filter source.txt
dotnet run --project examples/04-ValueShapes/Manual -- inspect --tag alpha --filter=recent source.txt
```
