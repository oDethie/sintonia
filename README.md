<p align="center">
  <img src="docs/icone.png" width="96" alt="Ícone do Sintonia">
</p>

<h1 align="center">Sintonia</h1>

<p align="center">
  Controle de som na bandeja do Windows: <b>saídas, microfones, volume de cada app e em qual saída cada app toca</b>.<br>
  Bonito, leve e sem instalação, para Windows 10 e 11.
</p>

---

## Por que existe

O controle de volume do Windows esconde o que mais importa: trocar a saída (fone, caixa, monitor), mudar o volume de **cada app** e mandar um app específico tocar em outra saída (ex.: o jogo no fone e o Discord na caixa). O Sintonia junta tudo isso num painel só, ao lado do relógio, no visual do Windows 11 e com o mínimo de consumo.

## Recursos

- **Painel na bandeja** com todas as saídas e microfones, cada um com volume e mudo.
- **Volume e mudo de cada app**, agrupados na saída onde o app está tocando.
- **Saída de cada app:** arraste o app para outra saída, ou use o **⋯** / clique direito no app. A escolha é **garantida**: se o Windows "esquecer", o Sintonia coloca o app de volta na saída certa.
- **Usar como padrão** com um botão próprio (sem trocar por engano). A saída padrão tem uma barrinha discreta na lateral.
- **Organize do seu jeito:** arraste as saídas para mudar a ordem, recolha/expanda com a setinha, renomeie ("Fone", "Caixa", "Monitor") e esconda o que não usa (o olhinho mostra os escondidos).
- **Perfis** (opcional, até 10): guardam a saída padrão, o microfone, a saída e o volume de cada app e como o painel está. Troque por botões, pelo menu do ícone ou por atalho. Podem **ativar sozinhos** quando um app (ex.: um jogo) começa a tocar e voltar ao anterior quando ele fecha.
- **Atalhos de teclado** configuráveis: abrir o painel, próxima saída, mudo geral e um por perfil.
- **Aviso na tela** discreto ao usar os atalhos (não aparece por cima de jogos em tela cheia).
- **Tema claro/escuro** que segue o Windows, cantos arredondados, animações suaves (opcionais).
- Funciona com monitores em **escalas diferentes** (ex.: 100% e 175%) e com a **barra de tarefas** fixa, que se esconde sozinha ou em qualquer lado da tela.
- **Configurações** numa janela própria (Geral, Perfis, Atalhos, Dispositivos), com tudo podendo ser ligado ou desligado.

## Como instalar

1. Baixe o **`Sintonia.exe`** na página de [**Releases**](../../releases/latest).
2. Coloque o arquivo numa pasta fixa, por exemplo `C:\Users\<você>\AppData\Local\Programs\Sintonia\`.
3. Dê dois cliques nele. O ícone do Sintonia aparece perto do relógio (e já fica fixado na barra).
4. Na primeira vez ele já passa a **iniciar com o Windows** (dá para desligar em Configurações ▸ Geral).

> **Aviso do Windows (SmartScreen):** como o programa é novo e não tem assinatura digital paga, o Windows pode mostrar *"O Windows protegeu o computador"*. Clique em **Mais informações ▸ Executar assim mesmo**.
>
> **Antivírus:** alguns antivírus desconfiam de programas pequenos e desconhecidos. O código está todo aqui no repositório para quem quiser conferir; se for bloqueado, adicione uma exceção para o `Sintonia.exe`.

Requisitos: Windows 10 (versão 2004 ou mais nova) ou Windows 11. O .NET Framework 4.8 necessário já vem no Windows.

## Como usar

| Onde | O que faz |
|---|---|
| **Clique no ícone** | Abre/fecha o painel |
| **Barra de volume** | Arraste para mudar o volume (a roda do mouse também funciona, se ligada) |
| **Ícone da saída / do app** | Liga/desliga o mudo |
| **Arrastar um app** | Manda o app tocar em outra saída |
| **Arrastar o nome de uma saída** | Muda a ordem da lista |
| **Setinha** | Recolhe/expande a saída |
| **Botão "Usar como padrão"** (aparece com o mouse em cima) | Deixa a saída como padrão do Windows |
| **Clique direito numa saída / app / perfil** | Opções daquele item |
| **Olhinho** ao lado de "Saída" | Mostra as saídas escondidas para trazê-las de volta |
| **Engrenagem** | Abre as Configurações |
| **Clique direito no ícone** | Menu rápido: saída, microfone, mudo, perfis, Configurações, Sair |

### Linha de comando (opcional)

| Comando | O que faz |
|---|---|
| `Sintonia.exe --perfil Nome` | Ativa um perfil (útil para atalhos e scripts) |
| `Sintonia.exe --config 0` | Abre as Configurações (0 Geral, 1 Perfis, 2 Atalhos, 3 Dispositivos) |
| `Sintonia.exe --diagnostico arquivo.txt` | Lista saídas, apps e a saída de cada app (não muda nada) |

## Privacidade

- **Não usa internet.** Não há nenhum código de rede no programa.
- Os atalhos usam o recurso oficial do Windows (`RegisterHotKey`). O programa **não lê o que você digita** nem vigia o mouse fora do painel.
- Tudo o que é gravado: o arquivo `%APPDATA%\Sintonia.ini` (configurações), `%APPDATA%\Sintonia.log` (só se acontecer algum erro), a tarefa **"Sintonia"** no Agendador de Tarefas (iniciar com o Windows), a preferência do Windows que deixa o ícone fixado na barra e, se você ativar, o atalho no menu Iniciar.

## Como funciona por dentro

- Volume, mudo e lista de apps: **Core Audio** do Windows (`IMMDeviceEnumerator`, `IAudioSessionManager2`, `ISimpleAudioVolume`).
- Saída padrão: `IPolicyConfig`. Saída de cada app: `IAudioPolicyConfigFactory` (interfaces internas do Windows, as mesmas usadas pelas Configurações de som e pelo EarTrumpet).
- O programa fica **parado** quando o painel está fechado: não há timers rodando; ele só acorda com um clique, um atalho ou um aviso do Windows (saída trocada, app começou a tocar).

## Consumo

Prioridade de processo baixa. Com o painel fechado: **0 ms de CPU** e poucos MB de RAM em uso. Não interfere em jogos.

## Compilar o código

Não precisa instalar nada: o Windows já traz o compilador C#.

```bat
build.bat
```

Gera o `Sintonia.exe` na mesma pasta. O código inteiro está em [`Sintonia.cs`](Sintonia.cs).

## Desinstalar

1. Em Configurações ▸ Geral, desligue **Iniciar com o Windows** e **Atalho no menu Iniciar**.
2. Clique com o botão direito no ícone ▸ **Sair**.
3. Apague o `Sintonia.exe` e os arquivos `%APPDATA%\Sintonia.ini` e `%APPDATA%\Sintonia.log`.

Não fica mais nada no sistema.

## Licença

[MIT](LICENSE): use, modifique e distribua à vontade.
