# Ranger

Widget para Windows que mostra a bateria dos dispositivos sem fio: o mouse Attack Shark X11 (lido direto do receptor, sem enviar nada ao mouse) e qualquer dispositivo Bluetooth que informe bateria ao Windows (fones, controles, teclados).

## Modos de exibição

Clique nos **⋯** (ou com o botão direito → *Configurações…*) para escolher:

- **Flutuante** — um traço fino na borda da tela que abre o painel ao passar o mouse. Dá para arrastar para cima e para baixo.
- **Barra de tarefas** — ícones com um anel de bateria fixos na barra, no lado esquerdo ou ao lado do relógio. Arraste os ícones para ajustar a posição.
- **Notch** — uma barrinha preta no topo da tela, como a do iPhone, que se abre ao passar o mouse.

O anel e a porcentagem ficam amarelos abaixo de 25 %, vermelhos abaixo de 10 % e verdes carregando. Quando uma bateria chega a 15 %, o Ranger se abre sozinho por alguns segundos.

## Compilar

Não precisa instalar nada: usa o compilador C# que já vem com o Windows (.NET Framework 4.x).

```powershell
.\build.ps1
```

Gera o `Ranger.exe` na pasta do projeto. As configurações ficam em `%APPDATA%\Ranger\settings.ini`.
