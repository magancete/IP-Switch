<div align="center">

<img src="docs/images/icon.svg" alt="Icono de IP Switch" width="96">

# IP Switch

**Cambia la configuración de red de Windows con un clic.**<br>
Guarda perfiles IPv4 por adaptador y aplícalos desde la app, la bandeja, un atajo, la línea de comandos o automáticamente al conectarte a una WiFi.

[![Build](https://github.com/magancete/IP-Switch/actions/workflows/build.yml/badge.svg)](https://github.com/magancete/IP-Switch/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/magancete/IP-Switch?label=versi%C3%B3n&color=0078D4)](https://github.com/magancete/IP-Switch/releases/latest)
![Windows 10 | 11](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows)
![Portable ~3 MB](https://img.shields.io/badge/portable-~3%20MB-success)
[![Licencia: MIT](https://img.shields.io/badge/licencia-MIT-yellow.svg)](LICENSE)

[**Descargar**](https://github.com/magancete/IP-Switch/releases/latest/download/IPSwitch.exe) ·
[Características](#características) ·
[Uso](#uso) ·
[Compilar](#compilar-desde-el-código) ·
[🇬🇧 English](README.md)

<picture>
  <source media="(prefers-color-scheme: light)" srcset="docs/images/home-light.png">
  <img src="docs/images/home.png" alt="Página de inicio de IP Switch con el estado de cada adaptador" width="820">
</picture>

</div>

> La documentación técnica ([arquitectura](docs/ARCHITECTURE.md), [changelog](CHANGELOG.md), [contribuir](CONTRIBUTING.md)) está en inglés. Esta guía resume el uso en español.

---

## ¿Por qué IP Switch?

Cambiar una IP fija en Windows obliga a pasar cada vez por *Panel de control → Conexiones de red → Propiedades → IPv4*. Si alternas entre la red de la oficina, un laboratorio con direcciones fijas y el DHCP de casa, **IP Switch lo reduce a un clic**, y si un perfil te deja sin conexión, **lo revierte automáticamente**.

- **Sin instalación.** Un único `.exe` de ~3 MB sobre .NET Framework 4.8, que ya viene con Windows 10 (1903+) y 11.
- **Seguro.** Comprueba la conectividad tras cada cambio, con cuenta atrás de reversión automática.
- **Aspecto nativo.** Diseño Fluent con Mica, tema claro y oscuro, como la Configuración de Windows 11.
- **Privado.** Sin telemetría; la única conexión opcional es la comprobación de actualizaciones en GitHub.

## Características

| Perfiles | Formas de aplicar |
|---|---|
| IP estática o DHCP, máscara, puerta de enlace con métrica, DNS | Ventana, vista reducida y panel de la bandeja |
| Direcciones IP y puertas de enlace adicionales (con métrica) | Atajos de teclado globales (p. ej. `Ctrl+Alt+1`) |
| Color, notas, duplicar, buscar y ordenar | Automáticamente al conectarse a una red WiFi |
| Detección del perfil activo | Línea de comandos `--apply "Oficina"` y accesos directos |
| Importar/exportar en JSON | Comprobación de conectividad con reversión automática |

**Adaptadores:** IP, máscara, puerta de enlace, DNS, MAC, velocidad y modo; renovar DHCP, reiniciar, habilitar/deshabilitar, vaciar la caché DNS, ocultar adaptadores (se recuerda) y abrir el panel de redes WiFi de Windows.

**Aplicación:** inglés y español con cambio en caliente, tema claro/oscuro/sistema, instancia única, inicio con Windows opcional, diseño responsivo y DPI por monitor.

## Capturas

<table>
<tr>
<td align="center"><img src="docs/images/profiles.png" alt="Tabla de perfiles" width="520"><br><sub><b>Perfiles</b></sub></td>
<td align="center"><img src="docs/images/compact.png" alt="Vista reducida" width="260"><br><sub><b>Vista reducida</b></sub></td>
</tr>
<tr>
<td align="center"><img src="docs/images/settings.png" alt="Ajustes" width="420"><br><sub><b>Ajustes</b></sub></td>
<td align="center"><img src="docs/images/editor.png" alt="Editor de perfiles" width="380"><br><sub><b>Editor de perfiles</b></sub></td>
</tr>
</table>

## Primeros pasos

1. Descarga [`IPSwitch.exe`](https://github.com/magancete/IP-Switch/releases/latest/download/IPSwitch.exe) de la última versión.
2. Colócalo donde quieras (p. ej. `C:\Herramientas\` o una memoria USB) y ejecútalo. Acepta el aviso UAC: cambiar la IP requiere administrador. Si lo vuelves a abrir mientras está en marcha, solo se trae al frente, sin otro aviso.
3. En **Inicio**, pulsa **Guardar como perfil** en un adaptador, o crea uno en **Perfiles → Añadir perfil**.

> [!TIP]
> SmartScreen puede avisar de que el ejecutable no está firmado: pulsa *Más información → Ejecutar de todas formas*. Puedes verificar la descarga con el fichero `IPSwitch.exe.sha256` publicado en cada versión.

**Requisitos:** Windows 10 1903 o posterior, o Windows 11 (probado en x64) · .NET Framework 4.8 (incluido) · permisos de administrador.

**Desinstalar:** desactiva *Iniciar con Windows* en Ajustes si lo activaste y borra `IPSwitch.exe` y la carpeta `%AppData%\IPSwitch`.

## Uso

### Aplicar un perfil con seguridad

1. Se guarda una instantánea de la configuración actual del adaptador.
2. Se aplica el perfil con `netsh` (dirección, IP adicionales y DNS).
3. Se hace ping a la puerta de enlace (o al host que elijas) durante unos segundos.
4. Si no responde, aparece **«¿Mantener esta configuración de red?»** y se restaura la anterior al agotarse la cuenta atrás.

### Vista reducida y bandeja

- **Vista reducida**: botón de la barra de título, junto a minimizar. Ventana pequeña y redimensionable con un botón por perfil; puede quedarse siempre visible y atenuarse cuando no está activa. Su alto se ajusta siempre a tus adaptadores.
- **Icono de bandeja**: clic = panel rápido; doble clic = ventana; clic derecho = *Aplicar perfil ▸*. El tooltip muestra el perfil activo o la IP de cada adaptador.

### Atajos y WiFi automática

En el editor (**Avanzado**) puedes asignar un atajo global con al menos un modificador y, en adaptadores WiFi, el SSID que aplica el perfil al conectarte a esa red.

### Línea de comandos

```text
IPSwitch.exe [opciones]

  --apply "Nombre"   Aplica el perfil y sale (0 = correcto, 1 = error, 2 = no existe)
  --minimized        Inicia oculto en el área de notificación
  --compact          Inicia en vista reducida
  --help             Muestra la ayuda
```

`--apply` sirve para scripts y el Programador de tareas. Si IP Switch ya está abierto, la orden se le reenvía y se devuelve su código de salida. *Crear acceso directo en el escritorio* (menú ⋯ de un perfil) crea un acceso que ejecuta `--apply` para ese perfil.

### Actualizaciones

Con *Buscar actualizaciones* activado, IP Switch te avisa cuando hay una versión nueva. En *Ajustes → Acerca de → Comprobar ahora*, **Actualizar ahora** descarga el nuevo `IPSwitch.exe` de GitHub, comprueba su SHA-256, sustituye el ejecutable y se reinicia; tus perfiles y ajustes siguen en `%AppData%\IPSwitch`. Si algo falla, sigue funcionando la versión actual sin cambios.

### Ajustes

| Sección | Opciones |
|---|---|
| Apariencia | Tema, idioma, siempre visible, atenuar la vista reducida |
| Página de perfiles | Columnas visibles |
| Comprobación de conexión | Activar, host a comprobar (vacío = puerta de enlace), tiempo antes de revertir |
| Sistema y comportamiento | Iniciar con Windows, clic en la bandeja, cerrar al área de notificación, confirmar al salir |
| Acerca de | Actualizaciones (aviso al iniciar y **actualización en un clic**), carpeta de datos, **informar de un error o proponer una mejora** |

## Datos y privacidad

| Fichero | Contenido |
|---|---|
| `%AppData%\IPSwitch\profiles.json` | Perfiles |
| `%AppData%\IPSwitch\settings.json` | Ajustes, incluidos los adaptadores ocultos |

- Los datos **nunca** se guardan junto al ejecutable. Los de versiones portables anteriores (`IPSwitch-data` junto al `.exe`) se copian la primera vez.
- La variable de entorno `IPSWITCH_DATA_DIR` cambia la carpeta (uso totalmente portable, pruebas).
- IP Switch no recopila nada. La comprobación de actualizaciones es opcional; solo se descarga una versión nueva al pulsar *Actualizar ahora*, y solo se instala si su SHA-256 coincide con el publicado. *Informar de un error* abre GitHub en tu navegador; no se envía nada automáticamente.

## Compilar desde el código

Requisitos: Windows, [.NET SDK](https://dotnet.microsoft.com/download) 8 o posterior y el *targeting pack* de .NET Framework 4.8.

```powershell
git clone https://github.com/magancete/IP-Switch.git
cd IP-Switch
./build.ps1          # comprobaciones + compilación Release (0 avisos) + tests → artifacts\IPSwitch.exe
```

Más detalles (estructura, pruebas, CI) en el [README en inglés](README.md#building-from-source) y en [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Solución de problemas

| Problema | Solución |
|---|---|
| «No se pudo aplicar…» con un mensaje de `netsh` | Es el mensaje de Windows. Revisa que la IP no esté repetida y que el adaptador esté habilitado. |
| Se revierte aunque la red funciona | La puerta de enlace no responde a ping. Indica otro host en *Ajustes → Host a comprobar* o desactiva la comprobación. |
| Un atajo no funciona | Otra aplicación lo usa (verás un aviso). Elige otro. |
| No arranca con Windows tras mover el `.exe` | Ábrelo una vez desde la nueva ubicación: la tarea de inicio se actualiza sola. |
| No aparece un adaptador | Puede estar oculto o ser un adaptador virtual excluido. |
| Al abrirlo no pasa nada | Ya está en ejecución: busca su icono en el área de notificación. |

## Contribuir

Errores, ideas y pull requests son bienvenidos: consulta [CONTRIBUTING.md](CONTRIBUTING.md). Desde la app, **Ajustes → Acerca de → Informar de un error o proponer una mejora** abre el [selector de issues](https://github.com/magancete/IP-Switch/issues/new/choose). Problemas de seguridad: [SECURITY.md](SECURITY.md).

## Licencia

[MIT](LICENSE) © 2026 Carlos Magán. Dependencias incrustadas, todas MIT: [WPF-UI](https://github.com/lepoco/wpfui), [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) y [Costura.Fody](https://github.com/Fody/Costura).
