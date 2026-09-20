# DiskLit

Analisador de espaço em disco para Windows. Mostra quais arquivos estão ocupando o disco usando enumeração Win32 de passagem única, sem criar um `FileInfo` por arquivo, e mantendo em memória apenas os 10.000 maiores resultados.

Estritamente somente leitura: o programa não apaga, move nem modifica nada.

## Requisitos

- Windows 10 ou 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)

## Executar

```powershell
dotnet run
```

## Gerar o executável

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

O executável fica em `publish\DiskLit.exe`. Para um binário que dispensa o runtime instalado, troque para `--self-contained true`.

## Como funciona

O scanner faz uma chamada nativa por pasta (`FindFirstFileEx` com `FindExInfoBasic` e `FIND_FIRST_EX_LARGE_FETCH`), que já devolve nome, tamanho e data de cada entrada. Não há uma segunda ida ao disco para obter metadados.

**Fila de trabalho compartilhada.** Os diretórios ficam numa pilha concorrente da qual todos os workers consomem, com um contador de pendências para detectar o término. Particionar apenas pelas pastas de primeiro nível não funciona: em discos reais uma única pasta costuma concentrar a maior parte do trabalho — medindo duas instalações, `C:\Users` respondeu por 81,6% e `E:\Windows` por 70,7% do tempo total, o que limitaria o ganho a 1,2x–1,4x independentemente do número de workers.

**Detecção de mídia.** O volume é aberto com `dwDesiredAccess = 0` para consultar `IOCTL_STORAGE_QUERY_PROPERTY` e ler a penalidade de busca. Pedir `GENERIC_READ` aqui exigiria privilégios de administrador e falharia com `ERROR_ACCESS_DENIED` numa execução normal. HDs com penalidade de busca são percorridos sequencialmente; SSD e NVMe usam de 2 a 6 workers, conforme o número de núcleos.

**Ciclos de travessia.** Junctions (`IO_REPARSE_TAG_MOUNT_POINT`) e links simbólicos (`IO_REPARSE_TAG_SYMLINK`) são ignorados para evitar ciclos e a travessia de outros volumes. Os demais reparse points são percorridos normalmente — descartar todos eles pelo atributo genérico faria o scanner pular pastas de nuvem, como a raiz do OneDrive, que carrega uma tag própria.

**Memória constante.** Um min-heap de 10.000 posições mantém apenas os maiores arquivos, então o consumo não cresce com o tamanho do disco.

**Progresso adaptativo.** Contadores são publicados a cada 650 ms; a lista parcial de resultados é materializada a cada 2 s, para não competir com a varredura.

Caminhos longos são enumerados pela sintaxe Win32 estendida (`\\?\`), e o manifesto declara `longPathAware`.

## Limitações conhecidas

- O filtro de busca percorre apenas os 10.000 maiores arquivos que estão na lista, não o disco inteiro.
- Os tamanhos são lógicos. Arquivos compactados ou esparsos aparecem pelo tamanho nominal, e hardlinks são contados uma vez por caminho.
- Pastas sem permissão de leitura são puladas e apenas somadas ao contador de inacessíveis. Em testes, o total varrido ficou entre 95% e 99% do espaço que o Windows reporta como usado.
- Não há ordenação por coluna nem visualização por pasta. O scanner já agrega os totais por pasta de primeiro nível durante a varredura, o que prepara esse recurso.

## Privacidade

- Não envia dados para a internet.
- Não altera nem apaga arquivos.
- Roda sem elevação (`asInvoker`).

## Licença

MIT. Veja [LICENSE](LICENSE).
