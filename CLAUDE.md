# WslManager — contexto do projeto

Gerenciador de distribuições WSL2. App nativo Windows em C# / WPF (.NET 10),
visual Fluent via WPF-UI, padrão MVVM via CommunityToolkit.Mvvm.

## Comandos

- Build: `dotnet build`
- Rodar: `dotnet run`
- Publicar: `dotnet publish -c Release`

Sempre rode `dotnet build` após qualquer alteração e corrija os erros antes
de seguir. O projeto só compila no Windows (WPF).

## Arquitetura

```
Core/          → interop pura, sem dependência de UI
  WslService.cs      ciclo de vida: enumerar (registro Lxss), wake, terminate,
                     shutdown, set-default, abrir terminal/Explorer
  Distro.cs          record imutável (snapshot de uma distro)
ViewModels/    → CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand])
  MainViewModel.cs   lista, timer de refresh (5s), ExecuteAsync centralizado
  DistroViewModel.cs comandos por distro
Views/         → XAML com WPF-UI (namespace http://schemas.lepo.co/wpfui/2022/xaml)
```

Regras de organização:
- Toda interop com wsl.exe/registro fica em `Core/`. ViewModels nunca chamam
  Process ou Registry diretamente.
- Novos serviços seguem o padrão: classe em `Core/`, injetada/instanciada no
  ViewModel, ações passam pelo `MainViewModel.ExecuteAsync` (confirmação
  opcional → trava UI → executa → mostra stderr em caso de falha → refresh).
- Strings de UI em português brasileiro.

## Armadilhas de interop (NÃO regredir nenhuma)

1. `wsl.exe` escreve UTF-16LE. Toda chamada precisa de `WSL_UTF8=1` no
   environment E `StandardOutputEncoding = Encoding.UTF8`. Já tratado em
   `WslService.RunAsync` — novas chamadas devem reusar esse método.
2. Deadlock de buffer: ler stdout e stderr de forma assíncrona ANTES do
   `WaitForExitAsync`. Nunca usar `ReadToEnd()` síncrono seguido de
   `WaitForExit()`.
3. `CreateNoWindow = true` em toda chamada, senão pisca console.
4. O projeto força `PlatformTarget=x64` e usa caminho absoluto de System32
   para o wsl.exe (redirecionamento WOW64). Não remover.
5. Polling seguro: apenas `wsl --list --running --quiet` e leitura do registro
   não acordam distros. Qualquer `wsl -d X -- comando` ACORDA a distro X e
   zera o vmIdleTimeout — nunca fazer isso em polling, só em ação explícita
   do usuário ou em distros já em execução.
6. Exit code 1 de `--list --running` significa "nenhuma distro rodando",
   não é erro.

## Regras de segurança do produto (invioláveis)

- `wsl --shutdown` derruba a VM inteira (todas as distros, incluindo
  docker-desktop): sempre com diálogo de confirmação explícito.
- Distros de sistema (docker-desktop, docker-desktop-data, rancher-desktop*,
  podman-machine-default): badge "sistema", confirmação extra ao encerrar,
  NUNCA oferecer unregister/apagar para elas.
- `wsl --unregister` é irreversível e apaga todos os dados: exigir que o
  usuário digite o nome exato da distro para habilitar o botão, e sempre
  oferecer "Exportar antes de apagar" no mesmo diálogo.
- Compactação de vhdx exige a distro parada: verificar estado e oferecer
  terminar antes.
- Antes de sobrescrever `.wslconfig`, criar backup `.wslconfig.bak-<timestamp>`.
- Import/clone: validar colisão de nome contra a lista existente ANTES de
  chamar o wsl.exe.

## Operações longas

Export, import, install de catálogo e compactação demoram minutos e o wsl.exe
não emite progresso. Padrão do projeto:
- Sem timeout (diferente do ExecuteAsync padrão de 60s) e com botão Cancelar
  que mata o processo filho.
- Progresso: barra indeterminada + quando houver arquivo de saída crescendo
  (.tar), exibir o tamanho atual via poll de 1s no FileInfo.Length.
- Nunca permitir duas operações longas simultâneas sobre a mesma distro.
