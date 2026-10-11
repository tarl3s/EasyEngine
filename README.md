# EasyEngine

**A engine de jogos 2D mais fácil de todas.** Você traz as artes, monta a cena arrastando e descreve em português o que cada coisa faz — a **IA escreve a lógica do jogo pra você**. No fim, exporta um `.exe` pronto pra jogar.

> Status: **v0.9** — conjunto de recursos completo, antes do primeiro jogo "de verdade" feito com ela. Rumo ao 1.0.

---

## O que ela faz

- **Editor visual de cenas** — paleta de objetos, arrastar e soltar, redimensionar, grade/snap, múltiplas camadas (inclusive camadas de UI fixas na tela), zoom e navegação.
- **IA local escreve o código** — selecione um objeto, escreva "pula e a cada 3 pulos dá um dash" e a IA aplica o comportamento de verdade. Também tem um modo **Brainstorm** pra conversar sobre ideias sem mexer no jogo.
- **Comportamentos prontos** — plataforma (pulo variável, coyote time, double jump, wall jump, dash), top-down, perseguição, patrulha, vida/dano/knockback, combate corpo a corpo, tiro, coletáveis, portas entre cenas, molas, empurrar blocos, e muito mais.
- **Mini-linguagem de regras** (`quando → faça`) pra lógica customizada, com variáveis globais, de cena e por objeto, inventário e diálogos.
- **Física** AABB com gravidade, plataformas um-sentido e plataformas móveis que carregam o player.
- **Animações** de sprite (idle/walk automático, flip), **tilemap** com pincel, **partículas**, **tweens** com easing, **barras** (vida/progresso) e mixagem de som por canal.
- **Geração procedural** (mapa estável por seed, ou que se recria a cada visita).
- **Estética customizável** — fontes, caixa de diálogo, cores, efeitos de texto (máquina de escrever, fade, piscar).
- **Export self-contained** — gera um `.exe` que roda sem precisar instalar nada (quando o SDK está presente na máquina que exporta).

## Requisitos

- **Windows** 10/11
- **.NET 8** — o SDK pra desenvolver/exportar, ou o Desktop Runtime pra rodar
- **[Ollama](https://ollama.com)** + um modelo baixado, pra parte de IA (veja abaixo)

## Como rodar (a partir do código-fonte)

```bash
dotnet build          # compila
dotnet run            # abre o menu inicial (launcher)
```

Modos de linha de comando:

- `dotnet run -- --play <projeto.eeproj>` — abre direto no jogo, sem editor
- `dotnet run -- --selftest` — teste automático (abre editor + roda uma cena, abre o console e toca os sons)
- `dotnet run -- --fulltest` — bateria completa headless (comportamentos, scripts, editor de blocos, ferramentas, E2E); log em `%TEMP%\easyengine_fulltest.log`
- `dotnet run -- --e2e` — joga o cenário "Axe Test" passo a passo, sem janela; log por etapa em `%TEMP%\easyengine_e2e.log`
- `dotnet run -- --scenarios` — gera o projeto **EasyEngine Tests** (um cenário por mecânica) na pasta `Jogos`
- `dotnet run -- --exprtest` / `--behtest` / `--aijsontest` / `--blocktest` — baterias específicas

## Testar mecânicas isoladas

No menu inicial, **🧪 Test scenarios** recria e abre o projeto *EasyEngine Tests*. Cada cena testa uma mecânica sozinha,
com um **checklist na tela** que marca ✓ sozinho a cada etapa. Primeiro cenário: **Axe Test**: escolher o machado,
golpear em qualquer lugar, derrubar uma árvore com 4 golpes e ver a pedra recusar o machado.

## Console de debug (F1)

Durante o teste de um jogo, **F1** (ou o botão *Console*) abre um console no estilo do Unity. Ele mostra o que o jogo está
fazendo: cliques, item na mão, golpes, acertos, regras de script disparadas, sons tocados, avisos e erros.
Tem filtros por nível e categoria, busca, contagem ×N para linhas repetidas e um painel de detalhes.
Ao fechar o jogo, a sessão é salva em `%TEMP%\easyengine_play.log`.
Nos scripts e no editor de blocos, a ação **Write to the console** (`log`) escreve sua própria linha, com `{variavel}`.

## Como a IA funciona

A IA roda **localmente** pela Ollama (em `localhost:11434`), então é **grátis e offline** — nada é enviado pra nuvem. O painel da IA tem um seletor de **Força da IA**:

| Força | Modelo | Observação |
|-------|--------|-----------|
| Leve | `qwen2.5-coder:1.5b` | Padrão. Rápido até em CPU. |
| Light+ | `qwen2.5-coder:3b` | Um pouco mais esperto, mais lento. |
| Médio | 7b | |
| Forte | 14b | Mais capaz, bem mais pesado. |
| Personalizado | (você digita) | Qualquer modelo instalado na Ollama. |

A engine não pede código C# ao modelo: ela manda o catálogo de comportamentos e o inventário da cena, e o modelo responde em **JSON** que é aplicado de forma defensiva (o parser tolera erros comuns dos modelos pequenos).

## Objetivo

Deixar qualquer pessoa — inclusive quem não gosta de programar — fazer seus jogos 2D. Por isso a IA é local e gratuita.

## Licença

A definir.
